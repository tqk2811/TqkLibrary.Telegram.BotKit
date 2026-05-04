using TqkLibrary.Telegram.BotKit.Handlers;
using TqkLibrary.Telegram.BotKit.Routing;

namespace TqkLibrary.Telegram.BotKit.Binding
{
    /// <summary>
    /// Builds the <see cref="ParameterBinding"/> array for a method + route template.
    /// Priority: route placeholder by name → well-known context types → error.
    /// For RouteToken bindings, reconciles the template constraint with the method param type:
    /// <list type="bullet">
    ///   <item>No constraint + method concrete T → effective = <c>[T]</c> (narrow, strict match).</item>
    ///   <item>No constraint + method <c>object</c> → throws (type cannot be inferred).</item>
    ///   <item>Constraint set + method <c>object</c> → effective = all declared types.</item>
    ///   <item>Constraint set + method concrete T present in declared → effective = <c>[T]</c> (narrow).</item>
    ///   <item>Constraint set + method concrete T not in declared → throws (conflict).</item>
    /// </list>
    /// RouteParameter.ClrTypes is updated so <see cref="RouteTemplate.TryMatch"/> is stricter.
    /// </summary>
    internal static class ParameterBindingFactory
    {
        public static ParameterBinding[] Build(MethodInfo method, RouteTemplate? template)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var bindings = new ParameterBinding[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo p = parameters[i];
                string name = p.Name ?? $"arg{i}";

                // [CommandArg] explicit binding for /command payload.
                if (p.GetCustomAttribute<CommandArgAttribute>(inherit: false) is not null)
                {
                    Type underlying = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
                    if (underlying != typeof(string))
                        throw new InvalidOperationException(
                            $"{method.DeclaringType?.Name}.{method.Name}: parameter '{name}' marked with [CommandArg] must be of type string or string?, not {p.ParameterType}.");
                    bindings[i] = new ParameterBinding
                    {
                        Kind = ParameterBindingKind.CommandArg,
                        ParameterType = p.ParameterType,
                        ParameterName = name,
                    };
                    continue;
                }

                // Context well-known types.
                ContextValue? ctx = MapContext(p.ParameterType);
                if (ctx.HasValue)
                {
                    bindings[i] = new ParameterBinding
                    {
                        Kind = ParameterBindingKind.Context,
                        ParameterType = p.ParameterType,
                        ParameterName = name,
                        ContextValue = ctx.Value,
                    };
                    continue;
                }

                // Route placeholder match by name.
                if (template is not null)
                {
                    RouteParameter? rp = null;
                    foreach (RouteParameter candidate in template.Parameters)
                    {
                        if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            rp = candidate;
                            break;
                        }
                    }
                    if (rp is not null)
                    {
                        Type[] effective = ResolveEffectiveTypes(rp, p.ParameterType, method);
                        rp.ApplyEffectiveTypes(effective);
                        bindings[i] = new ParameterBinding
                        {
                            Kind = ParameterBindingKind.RouteToken,
                            ParameterType = p.ParameterType,
                            ParameterName = name,
                            RouteName = rp.Name,
                            CandidateTypes = effective,
                        };
                        continue;
                    }
                }

                throw new InvalidOperationException(
                    $"Could not bind parameter '{name}' ({p.ParameterType}) of " +
                    $"{method.DeclaringType?.Name}.{method.Name}. " +
                    $"Either match a route placeholder name or use a supported type " +
                    $"(Update/Message/CallbackQuery/UpdateType/CancellationToken/ModuleContext/long ChatId/long TelegramUserId/string BotToken). " +
                    $"DI dependencies (including chat-state classes) must be injected via the module constructor.");
            }

            return bindings;
        }

        static Type[] ResolveEffectiveTypes(RouteParameter rp, Type methodParamType, MethodInfo method)
        {
            Type underlying = Nullable.GetUnderlyingType(methodParamType) ?? methodParamType;
            bool methodIsObject = underlying == typeof(object);

            if (rp.Constraint is null)
            {
                // No constraint — infer from the method param.
                if (methodIsObject)
                    throw new InvalidOperationException(
                        $"{method.DeclaringType?.Name}.{method.Name}: parameter '{rp.Name}' of type 'object' " +
                        $"requires an explicit constraint in the route template (e.g. '{{{rp.Name}:guid|int}}').");
                return [underlying];
            }

            // Parsed from the constraint — keep a snapshot for the narrow/throw logic below.
            Type[] declared = RouteParameterConverter.ResolveClrTypes(rp.Constraint);

            if (methodIsObject) return declared;

            foreach (Type d in declared)
                if (d == underlying) return [underlying];

            throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name}: parameter '{rp.Name}' " +
                $"({methodParamType.Name}) does not match the route template constraint '{rp.Constraint}'. " +
                $"Use 'object' for the method param or choose one of [{string.Join(", ", Array.ConvertAll(declared, t => t.Name))}].");
        }

        static ContextValue? MapContext(Type t)
        {
            if (t == typeof(Update)) return ContextValue.Update;
            if (t == typeof(Message)) return ContextValue.Message;
            if (t == typeof(CallbackQuery)) return ContextValue.CallbackQuery;
            if (t == typeof(UpdateType)) return ContextValue.UpdateType;
            if (t == typeof(CancellationToken)) return ContextValue.CancellationToken;
            if (t == typeof(ModuleContext)) return ContextValue.ModuleContext;
            // Don't map generic long/string to avoid confusion — users wanting ChatId/BotToken should read them via BaseTelegramHandler properties.
            return null;
        }
    }
}
