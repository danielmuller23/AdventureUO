// ADVENTURE: opcoes dos avisos estilo toast (Interface > Avisos Adventure)
using ClassicUO.Common;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.MyraWindows.Widgets;

namespace ClassicUO.Game.UI.MyraWindows.Options.Tabs;

/// <summary>Opcoes dos avisos (toasts) do shard Adventure: posicao e liga/desliga por tipo</summary>
public static class AdventureToastTab
{
    internal static IOptionSource GetContent()
    {
        Profile profile = ProfileManager.CurrentProfile;

        return OptionsUi.Vertical(
            OptionsUi.VisualContainer(
                new VisualContainerProps { LabelText = "Posicao dos avisos" },
                Option.ComboBox(
                    "Canto da tela",
                    new Accessor<int>(
                        () => profile.AdvToastCanto,
                        v =>
                        {
                            profile.AdvToastCanto = v;
                            AdventureToastGump.Reposicionar(World.Instance);
                        }
                    ),
                    [
                        "Superior direito",
                        "Superior esquerdo",
                        "Inferior direito",
                        "Inferior esquerdo",
                        "Personalizada (arrastada)"
                    ],
                    search: new SearchMetadata("Canto da tela", Keywords: ["toast", "aviso", "posicao"])
                ),
                Option.Button(
                    "Modo editar: arrastar um aviso de exemplo",
                    () => AdventureToastGump.ModoEditar(World.Instance),
                    search: new SearchMetadata("Modo editar avisos", Keywords: ["toast", "aviso", "arrastar"])
                ),
                Option.Button(
                    "Mostrar um aviso de teste",
                    () => AdventureToastGump.Teste(World.Instance),
                    search: new SearchMetadata("Aviso de teste", Keywords: ["toast", "aviso", "teste"])
                )
            ),
            OptionsUi.VisualContainer(
                new VisualContainerProps { LabelText = "Mostrar avisos de" },
                Option.Checkbox("Eventos", new Accessor<bool>(() => profile.AdvToastEvento), search: new SearchMetadata("Avisos de eventos", Keywords: ["toast", "aviso"])),
                Option.Checkbox("Bosses", new Accessor<bool>(() => profile.AdvToastBoss), search: new SearchMetadata("Avisos de bosses", Keywords: ["toast", "aviso"])),
                Option.Checkbox("Trofeus", new Accessor<bool>(() => profile.AdvToastTrofeu), search: new SearchMetadata("Avisos de trofeus", Keywords: ["toast", "aviso"])),
                Option.Checkbox("Level", new Accessor<bool>(() => profile.AdvToastLevel), search: new SearchMetadata("Avisos de level", Keywords: ["toast", "aviso"])),
                Option.Checkbox("Leilao e encomendas", new Accessor<bool>(() => profile.AdvToastLeilao), search: new SearchMetadata("Avisos de leilao e encomendas", Keywords: ["toast", "aviso"])),
                Option.Checkbox("Staff", new Accessor<bool>(() => profile.AdvToastStaff), search: new SearchMetadata("Avisos da staff", Keywords: ["toast", "aviso"]))
            )
        ).WithSearch(new SearchMetadata("Avisos Adventure", Keywords: ["toast", "aviso"]));
    }
}
