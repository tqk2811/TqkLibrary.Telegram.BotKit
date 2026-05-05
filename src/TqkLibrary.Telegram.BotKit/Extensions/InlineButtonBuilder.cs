using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.Extensions.Localization;
using TqkLibrary.Telegram.BotKit.Binding;
using TqkLibrary.Telegram.BotKit.Handlers;
using TqkLibrary.Telegram.BotKit.Routing;

namespace TqkLibrary.Telegram.BotKit.Extensions
{
    /// <summary>
    /// Renders callback data type-safely from an expression pointing to an <see cref="Attributes.InlineButtonAttribute"/> method.
    /// </summary>
    public static class InlineButtonBuilder
    {
        /// <summary>
        /// Builds callback data from an expression. The body must be a direct call to a module's action method
        /// (e.g. <c>c => c.Delete(walletId, default!, default)</c>).
        /// Context params (Update/Message/CallbackQuery/CancellationToken/...) accept any value — they are ignored at render time.
        /// </summary>
        public static string BuildCallbackData<TModule>(
            this ModuleActionRegistry registry,
            Expression<Func<TModule, Task>> expression)
            where TModule : CallbackModule
        {
            (MethodInfo method, object?[] args) = ExtractCall(expression);
            ActionDescriptor desc = registry.GetInlineDescriptor(method);
            return desc.RouteTemplate!.Format(BindRouteValues(method, args, desc.RouteTemplate));
        }

        /// <summary>
        /// Builds an <see cref="InlineKeyboardButton"/>.
        /// Text resolution order:
        /// <list type="number">
        ///   <item><paramref name="text"/> non-null (call-site override).</item>
        ///   <item><see cref="InlineButtonAttribute.TitleResourceType"/> + <see cref="InlineButtonAttribute.TitleResourceName"/>
        ///         using <paramref name="culture"/>.</item>
        ///   <item><paramref name="localizer"/>[<see cref="InlineButtonAttribute.TitleResourceName"/>] when the attribute
        ///         does not declare <c>TitleResourceType</c> (honors <see cref="CultureInfo.CurrentUICulture"/> only).</item>
        ///   <item><see cref="InlineButtonAttribute.Title"/> literal.</item>
        ///   <item>Fallback: the method name from the expression (<see cref="MethodInfo.Name"/>).</item>
        /// </list>
        /// </summary>
        public static InlineKeyboardButton ToInlineButton<TModule>(
            this ModuleActionRegistry registry,
            Expression<Func<TModule, Task>> expression,
            string? text = null,
            CultureInfo? culture = null,
            IStringLocalizer? localizer = null)
            where TModule : CallbackModule
        {
            (MethodInfo method, object?[] args) = ExtractCall(expression);
            ActionDescriptor desc = registry.GetInlineDescriptor(method);

            if (string.IsNullOrEmpty(text))
                text = LocalizedTextResolver.Resolve(
                    desc.InlineTitleResourceType, desc.InlineTitleResourceName, desc.InlineTitle, culture,
                    localizer);
            if (string.IsNullOrEmpty(text))
                text = method.Name;

            return InlineKeyboardButton.WithCallbackData(text, desc.RouteTemplate!.Format(BindRouteValues(method, args, desc.RouteTemplate)));
        }

        // ── Expression tree extraction ────────────────────────────────────────

        static (MethodInfo method, object?[] args) ExtractCall(LambdaExpression expr)
        {
            if (expr.Body is not MethodCallExpression call)
                throw new ArgumentException(
                    "Expression must be a direct method call, e.g.: c => c.Delete(id, default!, default).",
                    nameof(expr));

            object?[] args = new object?[call.Arguments.Count];
            for (int i = 0; i < call.Arguments.Count; i++)
                args[i] = EvaluateExpression(call.Arguments[i]);
            return (call.Method, args);
        }

        /// <summary>
        /// Evaluate an argument-position expression without compiling, when possible.
        /// Handles the patterns the builder actually sees in practice:
        ///   - <c>ConstantExpression</c>: literal values, captured-closure root.
        ///   - <c>DefaultExpression</c>: <c>default!</c> / <c>default(T)</c>.
        ///   - <c>MemberExpression</c>: closure field, property chain — recurse + reflect.
        ///   - <c>UnaryExpression</c> Convert/ConvertChecked: recurse + apply the cast. Built-in conversions
        ///     (numeric, enum&lt;-&gt;primitive, boxing, nullable) go through <see cref="ApplyConvert"/>; user-defined
        ///     <c>implicit</c>/<c>explicit operator</c> conversions go through <see cref="InvokeConversionOperator"/>
        ///     which JIT-compiles a typed delegate per <see cref="MethodInfo"/> and caches it.
        ///   - <c>UnaryExpression</c> Quote: recurse on the operand (no value transform).
        /// Anything else (method calls, lambdas) falls back to <c>Expression.Compile()</c>.
        /// </summary>
        static object? EvaluateExpression(Expression expr)
        {
            switch (expr.NodeType)
            {
                case ExpressionType.Constant:
                    return ((ConstantExpression)expr).Value;

                case ExpressionType.Default:
                    Type defaultType = ((DefaultExpression)expr).Type;
                    return defaultType.IsValueType && Nullable.GetUnderlyingType(defaultType) is null
                        ? Activator.CreateInstance(defaultType)
                        : null;

                case ExpressionType.MemberAccess:
                    var me = (MemberExpression)expr;
                    object? target = me.Expression is null ? null : EvaluateExpression(me.Expression);
                    return me.Member switch
                    {
                        FieldInfo f => f.GetValue(target),
                        PropertyInfo p => p.GetValue(target),
                        _ => CompileEvaluate(expr),
                    };

                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                {
                    var unary = (UnaryExpression)expr;
                    object? operand = EvaluateExpression(unary.Operand);
                    return unary.Method is null
                        ? ApplyConvert(operand, unary.Type)
                        : InvokeConversionOperator(unary.Method, operand);
                }

                case ExpressionType.Quote:
                    return EvaluateExpression(((UnaryExpression)expr).Operand);

                default:
                    return CompileEvaluate(expr);
            }
        }

        /// <summary>
        /// Apply a runtime cast equivalent to a C# <c>Convert</c> expression node — for built-in conversions
        /// only (<see cref="UnaryExpression.Method"/> is null). Required so explicit casts in the user's
        /// expression (e.g. <c>(int)g.SiteName</c>) propagate the destination type into the formatter —
        /// otherwise the underlying enum would leak through and the route would render the enum name instead
        /// of the integer the method signature is binding against.
        /// </summary>
        static object? ApplyConvert(object? value, Type targetType)
        {
            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value is null) return null;
            if (underlying.IsInstanceOfType(value)) return value;
            if (underlying.IsEnum) return Enum.ToObject(underlying, value);
            if (value is Enum && underlying.IsPrimitive)
                return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            if (value is IConvertible)
            {
                try { return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture); }
                catch (InvalidCastException) { return value; }
                catch (FormatException) { return value; }
                catch (OverflowException) { return value; }
            }
            return value;
        }

        // Cache one compiled invoker per user-defined conversion operator (e.g. WalletId.op_Explicit → Guid).
        // First call compiles a typed lambda (≈30 μs amortized). Subsequent calls are a dict lookup + delegate call (≈60 ns)
        // — orders of magnitude faster than MethodInfo.Invoke (~200 ns + args-array allocation each call) and
        // Expression.Compile() per call (~30 μs each).
        static readonly ConcurrentDictionary<MethodInfo, Func<object?, object?>> _conversionInvokerCache = new();

        /// <summary>
        /// Invoke a user-defined <c>implicit</c>/<c>explicit operator</c> conversion (the <c>Method</c> on a
        /// <c>Convert</c>/<c>ConvertChecked</c> node). The compiled delegate is cached per
        /// <see cref="MethodInfo"/>, so the JIT-compile cost is paid once per conversion operator across the
        /// process lifetime regardless of how many times any given button is built.
        /// </summary>
        static object? InvokeConversionOperator(MethodInfo method, object? operand)
        {
            Func<object?, object?> invoker = _conversionInvokerCache.GetOrAdd(method, BuildConversionInvoker);
            return invoker(operand);
        }

        static Func<object?, object?> BuildConversionInvoker(MethodInfo method)
        {
            // Build:  (object? x) => (object?)method((TSource)x)
            Type sourceType = method.GetParameters()[0].ParameterType;
            ParameterExpression boxedInput = Expression.Parameter(typeof(object), "x");
            Expression body = Expression.Convert(
                Expression.Call(method, Expression.Convert(boxedInput, sourceType)),
                typeof(object));
            return Expression.Lambda<Func<object?, object?>>(body, boxedInput).Compile();
        }

        static object? CompileEvaluate(Expression expr)
        {
            LambdaExpression lambda = Expression.Lambda(expr);
            return lambda.Compile().DynamicInvoke();
        }

        static Dictionary<string, object?> BindRouteValues(MethodInfo method, object?[] args, RouteTemplate template)
        {
            ParameterInfo[] paramInfos = method.GetParameters();
            Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < paramInfos.Length; i++)
            {
                string name = paramInfos[i].Name ?? $"arg{i}";
                foreach (RouteParameter rp in template.Parameters)
                {
                    if (string.Equals(rp.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        values[rp.Name] = args[i];
                        break;
                    }
                }
            }
            return values;
        }
    }
}
