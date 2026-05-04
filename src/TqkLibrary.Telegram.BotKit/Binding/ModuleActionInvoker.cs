using TqkLibrary.Telegram.BotKit.Handlers;
using TqkLibrary.Telegram.BotKit.Routing;

namespace TqkLibrary.Telegram.BotKit.Binding
{
    /// <summary>
    /// Instantiates the module via <see cref="ActivatorUtilities"/>, binds parameters, and invokes the action.
    /// </summary>
    internal static class ModuleActionInvoker
    {
        public static async Task InvokeAsync(
            ActionDescriptor descriptor,
            IServiceProvider scopedProvider,
            UpdateContext update)
        {
            object handler = ActivatorUtilities.CreateInstance(scopedProvider, descriptor.ModuleType);
            if (handler is BaseTelegramHandler baseHandler)
                baseHandler.ModuleContext = update.Module;

            object?[] args = BindArguments(descriptor, update);

            object? result = descriptor.Method.Invoke(handler, args);
            if (result is Task task) await task.ConfigureAwait(false);
            else if (result is ValueTask vt) await vt.ConfigureAwait(false);
            // else: sync action — discouraged but not blocked.
        }

        static object?[] BindArguments(ActionDescriptor descriptor, UpdateContext update)
        {
            ParameterBinding[] bindings = descriptor.Parameters;
            var args = new object?[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                ParameterBinding b = bindings[i];
                args[i] = b.Kind switch
                {
                    ParameterBindingKind.Context => ResolveContext(b, update),
                    ParameterBindingKind.RouteToken => ResolveRouteToken(b, update),
                    ParameterBindingKind.CommandArg => update.CommandArgs,
                    _ => throw new InvalidOperationException($"Unknown binding kind {b.Kind}")
                };
            }
            return args;
        }

        static object? ResolveContext(ParameterBinding b, UpdateContext update) => b.ContextValue switch
        {
            ContextValue.Update => update.Update,
            ContextValue.Message => update.Message,
            ContextValue.CallbackQuery => update.CallbackQuery,
            ContextValue.UpdateType => update.UpdateType,
            ContextValue.CancellationToken => update.CancellationToken,
            ContextValue.ModuleContext => update.Module,
            _ => throw new InvalidOperationException($"Unknown context value {b.ContextValue}")
        };

        static object? ResolveRouteToken(ParameterBinding b, UpdateContext update)
        {
            IReadOnlyDictionary<string, string> values = update.RouteValues
                ?? throw new InvalidOperationException(
                    $"Parameter '{b.ParameterName}' requires route values but the dispatch has no route match.");
            if (!values.TryGetValue(b.RouteName!, out string? raw))
                throw new InvalidOperationException(
                    $"Parameter '{b.ParameterName}' has no corresponding route value (route name '{b.RouteName}').");

            // Iterate candidates (single concrete type, or multiple types for an 'object' param).
            // Registry has already narrowed concrete params to [methodParamType] → exactly one attempt.
            Type[] candidates = b.CandidateTypes ?? [b.ParameterType];
            foreach (Type t in candidates)
            {
                if (RouteParameterConverter.TryParse(raw, t, out object? value))
                    return value;
            }
            throw new InvalidOperationException(
                $"Could not parse '{raw}' into {b.ParameterType} for parameter '{b.ParameterName}' " +
                $"(candidates: {string.Join(", ", Array.ConvertAll(candidates, t => t.Name))}).");
        }
    }
}
