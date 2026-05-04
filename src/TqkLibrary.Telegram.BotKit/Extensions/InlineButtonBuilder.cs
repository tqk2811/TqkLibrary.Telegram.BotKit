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

        static object? EvaluateExpression(Expression expr)
        {
            if (expr is ConstantExpression ce) return ce.Value;
            // Compile + invoke: covers field/property/method-call closures. Cost is fine — used only when building a button, not on the hot path.
            var lambda = Expression.Lambda(expr);
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
