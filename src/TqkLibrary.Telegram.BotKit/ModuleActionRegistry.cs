using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Localization;
using TqkLibrary.Telegram.BotKit.Binding;
using TqkLibrary.Telegram.BotKit.Extensions;
using TqkLibrary.Telegram.BotKit.Handlers;
using TqkLibrary.Telegram.BotKit.Routing;

[assembly: InternalsVisibleTo("TqkLibrary.Telegram.BotKit.Tests")]

namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Scans assemblies at startup → builds the action descriptor table.
    /// Singleton. Queries must go through the lookup methods below; the raw dictionaries are kept
    /// internal so we can optimize them later (trie, cache, etc.).
    /// </summary>
    public sealed class ModuleActionRegistry
    {
        // command name (lowercase) → descriptor
        readonly Dictionary<string, ActionDescriptor> _commandByName = new(StringComparer.OrdinalIgnoreCase);
        // OnUserInput key (case-sensitive) → descriptor
        readonly Dictionary<string, ActionDescriptor> _userInputByKey = new(StringComparer.Ordinal);
        // regex descriptors (ordered)
        readonly List<ActionDescriptor> _regexes = new();
        // inline button descriptors grouped by first-literal prefix for fast lookup
        readonly Dictionary<string, List<ActionDescriptor>> _inlineByPrefix = new(StringComparer.OrdinalIgnoreCase);
        // MethodInfo → list of descriptors for that method (to look up template when rendering via expression)
        readonly Dictionary<MethodInfo, List<ActionDescriptor>> _byMethod = new();
        // all handler types (for DI registration) — includes both concrete Command and Module types
        readonly HashSet<Type> _handlerTypes = new();
        // ModuleType → cached factory; avoids re-running ActivatorUtilities constructor scan per dispatch
        readonly Dictionary<Type, Func<IServiceProvider, object>> _moduleFactories = new();

        public IReadOnlyCollection<Type> HandlerTypes => _handlerTypes;

        internal ModuleActionRegistry(
            IEnumerable<Assembly> moduleAssemblies,
            IEnumerable<Type> moduleTypes,
            IEnumerable<Type> commandTypes)
        {
            HashSet<Type> scanned = new();

            foreach (Assembly asm in moduleAssemblies.Distinct())
                foreach (Type type in GetLoadableTypes(asm))
                    if (!type.IsAbstract && typeof(CallbackModule).IsAssignableFrom(type) && scanned.Add(type))
                        ScanHandler(type);

            foreach (Type t in moduleTypes)
                if (!t.IsAbstract && typeof(CallbackModule).IsAssignableFrom(t) && scanned.Add(t))
                    ScanHandler(t);

            foreach (Type t in commandTypes)
                if (!t.IsAbstract && typeof(CommandModule).IsAssignableFrom(t) && scanned.Add(t))
                    ScanHandler(t);

            // Sort each prefix by descending "specificity" — templates with more literal segments
            // are tried first, preventing a placeholder {x} from swallowing a literal under the same prefix.
            foreach (List<ActionDescriptor> list in _inlineByPrefix.Values)
                list.Sort((a, b) => CountLiterals(b.RouteTemplate!).CompareTo(CountLiterals(a.RouteTemplate!)));

            // Sort regex descriptors by Order ascending (lower runs first) — the dispatcher iterates
            // in order and honors StopOnMatch to bail out early when needed.
            _regexes.Sort((a, b) => a.RegexOrder.CompareTo(b.RegexOrder));
        }

        static int CountLiterals(RouteTemplate t) => t.SegmentCount - t.Parameters.Count;

        /// <summary>Test-only: build a registry from a specific list of types (skips assembly scan).</summary>
        internal static ModuleActionRegistry ForTests(params Type[] handlerTypes)
        {
            var registry = new ModuleActionRegistry(Array.Empty<Assembly>(), Array.Empty<Type>(), Array.Empty<Type>());
            foreach (Type t in handlerTypes)
                if (typeof(BaseTelegramHandler).IsAssignableFrom(t) && !t.IsAbstract)
                    registry.ScanHandler(t);
            foreach (List<ActionDescriptor> list in registry._inlineByPrefix.Values)
                list.Sort((a, b) => CountLiterals(b.RouteTemplate!).CompareTo(CountLiterals(a.RouteTemplate!)));
            registry._regexes.Sort((a, b) => a.RegexOrder.CompareTo(b.RegexOrder));
            return registry;
        }

        void ScanHandler(Type handlerType)
        {
            bool isCommand = typeof(CommandModule).IsAssignableFrom(handlerType);
            bool isModule = typeof(CallbackModule).IsAssignableFrom(handlerType);

            CallbackPrefixAttribute? prefixAttr = handlerType.GetCustomAttribute<CallbackPrefixAttribute>(inherit: false);
            if (prefixAttr is not null && !isModule)
                throw new InvalidOperationException(
                    $"{handlerType.Name}: [{nameof(CallbackPrefixAttribute)}] is only valid on {nameof(CallbackModule)}. " +
                    $"Remove the attribute or change the base class.");

            bool added = false;
            foreach (MethodInfo method in handlerType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.DeclaringType == typeof(object)) continue;
                if (method.DeclaringType == typeof(BaseTelegramHandler)) continue;
                if (method.DeclaringType == typeof(CallbackModule)) continue;
                if (method.DeclaringType == typeof(CommandModule)) continue;

                TelegramCommandAttribute[] cmdAttrs = method.GetCustomAttributes<TelegramCommandAttribute>(inherit: false).ToArray();
                InlineButtonAttribute[] ibAttrs = method.GetCustomAttributes<InlineButtonAttribute>(inherit: false).ToArray();
                OnUserInputAttribute[] inputAttrs = method.GetCustomAttributes<OnUserInputAttribute>(inherit: false).ToArray();
                TelegramRegexAttribute[] rxAttrs = method.GetCustomAttributes<TelegramRegexAttribute>(inherit: false).ToArray();

                if (cmdAttrs.Length == 0 && ibAttrs.Length == 0 && inputAttrs.Length == 0 && rxAttrs.Length == 0)
                    continue;

                // Validate: TelegramCommand attrs only on CommandModule; Inline/UserInput/Regex only on CallbackModule.
                if (cmdAttrs.Length > 0 && !isCommand)
                    throw new InvalidOperationException(
                        $"{handlerType.Name}.{method.Name}: [{nameof(TelegramCommandAttribute)}] is only valid on {nameof(CommandModule)}.");
                if ((ibAttrs.Length > 0 || inputAttrs.Length > 0 || rxAttrs.Length > 0) && !isModule)
                    throw new InvalidOperationException(
                        $"{handlerType.Name}.{method.Name}: [InlineButton]/[OnUserInput]/[TelegramRegex] are only valid on {nameof(CallbackModule)}.");

                Func<object, object?[], object?>? invoker = null;
                Func<object, object?[], object?> GetInvoker() => invoker ??= InvokerFactory.Create(method);
                Func<IServiceProvider, object> moduleFactory = GetOrCreateModuleFactory(handlerType);

                foreach (TelegramCommandAttribute a in cmdAttrs)
                {
                    ParameterBinding[] parms = ParameterBindingFactory.Build(method, template: null);
                    var desc = new ActionDescriptor
                    {
                        Kind = ActionKind.Command,
                        ModuleType = handlerType,
                        Method = method,
                        Parameters = parms,
                        Invoker = GetInvoker(),
                        ModuleFactory = moduleFactory,
                        CommandName = a.Name,
                        CommandOrder = a.Order,
                        CommandDescription = a.Description,
                        CommandDescriptionResourceType = a.DescriptionResourceType,
                        CommandDescriptionResourceName = a.DescriptionResourceName,
                    };
                    if (!_commandByName.TryAdd(a.Name, desc))
                        throw new InvalidOperationException(
                            $"Command '/{a.Name}' is already registered by {_commandByName[a.Name].ModuleType.Name}.{_commandByName[a.Name].Method.Name}, conflicting with {handlerType.Name}.{method.Name}.");
                    RegisterByMethod(method, desc);
                    added = true;
                }

                foreach (InlineButtonAttribute a in ibAttrs)
                {
                    string fullTemplate = prefixAttr is null
                        ? a.Template
                        : $"{prefixAttr.Prefix}/{a.Template}";
                    RouteTemplate template = RouteTemplate.Parse(fullTemplate);
                    ParameterBinding[] parms = ParameterBindingFactory.Build(method, template);
                    var desc = new ActionDescriptor
                    {
                        Kind = ActionKind.InlineButton,
                        ModuleType = handlerType,
                        Method = method,
                        Parameters = parms,
                        Invoker = GetInvoker(),
                        ModuleFactory = moduleFactory,
                        RouteTemplate = template,
                        InlineTitle = a.Title,
                        InlineTitleResourceType = a.TitleResourceType,
                        InlineTitleResourceName = a.TitleResourceName,
                    };
                    if (!_inlineByPrefix.TryGetValue(template.Prefix, out List<ActionDescriptor>? list))
                    {
                        list = new List<ActionDescriptor>();
                        _inlineByPrefix[template.Prefix] = list;
                    }
                    list.Add(desc);
                    RegisterByMethod(method, desc);
                    added = true;
                }

                foreach (OnUserInputAttribute a in inputAttrs)
                {
                    ParameterBinding[] parms = ParameterBindingFactory.Build(method, template: null);
                    var desc = new ActionDescriptor
                    {
                        Kind = ActionKind.UserInput,
                        ModuleType = handlerType,
                        Method = method,
                        Parameters = parms,
                        Invoker = GetInvoker(),
                        ModuleFactory = moduleFactory,
                        UserInputKey = a.Key,
                    };
                    if (!_userInputByKey.TryAdd(a.Key, desc))
                        throw new InvalidOperationException(
                            $"OnUserInput key '{a.Key}' is already registered by {_userInputByKey[a.Key].ModuleType.Name}.{_userInputByKey[a.Key].Method.Name}.");
                    RegisterByMethod(method, desc);
                    added = true;
                }

                foreach (TelegramRegexAttribute a in rxAttrs)
                {
                    ParameterBinding[] parms = ParameterBindingFactory.Build(method, template: null);
                    var desc = new ActionDescriptor
                    {
                        Kind = ActionKind.Regex,
                        ModuleType = handlerType,
                        Method = method,
                        Parameters = parms,
                        Invoker = GetInvoker(),
                        ModuleFactory = moduleFactory,
                        Regex = a.Regex,
                        RegexOrder = a.Order,
                        RegexStopOnMatch = a.StopOnMatch,
                    };
                    _regexes.Add(desc);
                    RegisterByMethod(method, desc);
                    added = true;
                }
            }

            if (added) _handlerTypes.Add(handlerType);
        }

        Func<IServiceProvider, object> GetOrCreateModuleFactory(Type handlerType)
        {
            if (!_moduleFactories.TryGetValue(handlerType, out Func<IServiceProvider, object>? factory))
            {
                ObjectFactory raw;
                try
                {
                    raw = ActivatorUtilities.CreateFactory(handlerType, Type.EmptyTypes);
                }
                catch (Exception ex)
                {
                    // ActivatorUtilities throws InvalidOperationException for missing public ctor,
                    // ambiguous best ctor, or non-injectable parameter type — all of which surface
                    // as a stack trace inside the framework with no clue which handler is at fault.
                    // Re-throw with the handler's full name + a pointer to the likely cause.
                    throw new InvalidOperationException(
                        $"Could not build constructor factory for handler '{handlerType.FullName}'. " +
                        $"BotKit handlers must expose a single public constructor whose parameters are all " +
                        $"resolvable from the DI container. See inner exception for the original error.",
                        ex);
                }
                factory = sp => raw(sp, null);
                _moduleFactories[handlerType] = factory;
            }
            return factory;
        }

        void RegisterByMethod(MethodInfo method, ActionDescriptor desc)
        {
            if (!_byMethod.TryGetValue(method, out List<ActionDescriptor>? list))
            {
                list = new List<ActionDescriptor>();
                _byMethod[method] = list;
            }
            list.Add(desc);
        }

        // ── Lookup ────────────────────────────────────────────────────────────

        public ActionDescriptor? FindCommand(string name)
            => _commandByName.TryGetValue(name, out ActionDescriptor? d) ? d : null;

        public ActionDescriptor? FindUserInputHandler(string key)
            => _userInputByKey.TryGetValue(key, out ActionDescriptor? d) ? d : null;

        /// <summary>True if any module declared at least one <see cref="OnUserInputAttribute"/> handler.</summary>
        public bool HasUserInputHandlers => _userInputByKey.Count > 0;

        public IReadOnlyList<ActionDescriptor> AllRegexes => _regexes;

        /// <summary>Find the inline button descriptor matching the callback data. Returns the descriptor and the captured values.</summary>
        public (ActionDescriptor descriptor, IReadOnlyDictionary<string, string> values)? MatchInlineButton(string callbackData)
        {
            if (callbackData is null) return null;

            // Split once and reuse — every candidate descriptor under the same prefix matches against
            // the same parts array, so we avoid a per-descriptor string.Split allocation on the hot path.
            string[] parts = callbackData.Split(RouteTemplate.Separators);
            string prefix = parts[0];

            if (!_inlineByPrefix.TryGetValue(prefix, out List<ActionDescriptor>? list))
                return null;

            foreach (ActionDescriptor d in list)
            {
                if (d.RouteTemplate!.TryMatchParts(parts, out IReadOnlyDictionary<string, string> values))
                    return (d, values);
            }
            return null;
        }

        /// <summary>Returns the route template for a method via the expression helper (throws when the method has no InlineButton).</summary>
        public RouteTemplate GetInlineTemplate(MethodInfo method) => GetInlineDescriptor(method).RouteTemplate!;

        internal ActionDescriptor GetInlineDescriptor(MethodInfo method)
        {
            if (!_byMethod.TryGetValue(method, out List<ActionDescriptor>? list))
                throw new InvalidOperationException(
                    $"Method {method.DeclaringType?.Name}.{method.Name} is not registered in the registry.");
            foreach (ActionDescriptor d in list)
                if (d.Kind == ActionKind.InlineButton) return d;
            throw new InvalidOperationException(
                $"Method {method.DeclaringType?.Name}.{method.Name} has no [{nameof(InlineButtonAttribute)}].");
        }

        /// <summary>
        /// List of <see cref="BotCommand"/> for SetMyCommands (filtered by non-empty description,
        /// sorted by Order). Description is resolved via <see cref="LocalizedTextResolver"/> —
        /// priority: attribute resourceType → fallbackLocalizer → literal.
        /// </summary>
        public IEnumerable<BotCommand> GetBotCommands(CultureInfo? culture = null, IStringLocalizer? fallbackLocalizer = null)
            => GetBotCommandsCore(commandType: null, culture, fallbackLocalizer);

        /// <summary>
        /// Variant filtered by a specific command class — returns only the <c>/command</c>s declared in
        /// <paramref name="commandType"/>. Useful when the app has multiple <see cref="CommandModule"/>
        /// classes and SetMyCommands should publish only a subset to a particular bot.
        /// </summary>
        public IEnumerable<BotCommand> GetBotCommands(Type commandType, CultureInfo? culture = null, IStringLocalizer? fallbackLocalizer = null)
        {
            if (commandType is null) throw new ArgumentNullException(nameof(commandType));
            return GetBotCommandsCore(commandType, culture, fallbackLocalizer);
        }

        IEnumerable<BotCommand> GetBotCommandsCore(Type? commandType, CultureInfo? culture, IStringLocalizer? fallbackLocalizer)
        {
            IEnumerable<ActionDescriptor> source = _commandByName.Values;
            if (commandType is not null)
                source = source.Where(x => x.ModuleType == commandType);

            foreach (ActionDescriptor d in source
                .OrderBy(x => x.CommandOrder)
                .ThenBy(x => x.CommandName, StringComparer.Ordinal))
            {
                string? desc = LocalizedTextResolver.Resolve(
                    d.CommandDescriptionResourceType,
                    d.CommandDescriptionResourceName,
                    d.CommandDescription,
                    culture,
                    fallbackLocalizer);
                if (!string.IsNullOrWhiteSpace(desc))
                    yield return new BotCommand { Command = d.CommandName!, Description = desc };
            }
        }

        static IEnumerable<Type> GetLoadableTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
        }
    }
}
