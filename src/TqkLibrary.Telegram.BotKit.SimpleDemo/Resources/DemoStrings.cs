using System.Resources;

namespace TqkLibrary.Telegram.BotKit.SimpleDemo
{
    /// <summary>
    /// Resource discriminator for the demo project. Two roles:
    /// 1. <see cref="ResourceManager"/> property is consumed by BotKit's
    ///    <see cref="LocalizedTextResolver"/> when an attribute references this type, e.g.
    ///    <c>[TelegramCommand("start", DescriptionResourceType = typeof(DemoStrings), DescriptionResourceName = "DescStart")]</c>.
    /// 2. Generic argument for <c>IStringLocalizer&lt;DemoStrings&gt;</c>.
    ///
    /// Lives in the root project namespace. The base name below matches the SDK default
    /// embedded-resource manifest for resx files without a paired .Designer.cs:
    /// <c>{RootNamespace}.{filename}.resources</c> — the <c>Resources/</c> subfolder is
    /// NOT reflected in the manifest path. Culture-specific .vi.resx is auto-routed to a
    /// satellite assembly with manifest <c>{base}.vi.resources</c>; <see cref="ResourceManager"/>
    /// expects exactly that culture-suffixed name during satellite probing — do not pin
    /// <c>LogicalName</c> in the csproj or that probe breaks. <c>AddLocalization()</c> is
    /// called without a <c>ResourcesPath</c> so <c>IStringLocalizer&lt;DemoStrings&gt;</c>
    /// composes the same base name (<c>{rootNamespace}.{trimmed type name}</c>).
    /// </summary>
    public sealed class DemoStrings
    {
        // Non-static so IStringLocalizer<DemoStrings> can take it as a type argument.
        // Sealed + private ctor = no instantiation in user code; the type is purely a marker.
        DemoStrings() { }

        public static ResourceManager ResourceManager { get; } = new ResourceManager(
            "TqkLibrary.Telegram.BotKit.SimpleDemo.DemoStrings",
            typeof(DemoStrings).Assembly);
    }
}
