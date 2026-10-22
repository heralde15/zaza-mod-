using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace Terraria.Unified.Features;

/// <summary>
/// Módulo separado de integración Zaza para HERO's Mod.
/// Esta primera capa conecta la navegación de Zaza con funciones que ya existen en HERO's.
/// No depende de los tipos tModLoader de Terraria.ZazaMod, que aún necesitan un port independiente.
/// </summary>
internal static partial class HerosModPanel
{
    private static void DrawZazaModTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Zaza Mod · módulos de integración");
        Utils.DrawBorderString(sb,
            "Selecciona un módulo para abrir la herramienta correspondiente de HERO's Mod.",
            new Vector2(panel.X + Pad, y), Color.White, 0.70f);
        y += 30;

        int gap = 8;
        int bw = (Width - Pad * 2 - gap) / 2;
        int h = RowH + 8;

        DrawZazaModuleButton(sb, pixel, panel, ref y, mouse, bw, h, gap,
            "Jugador y utilidades", "Herramientas del jugador", Tab.Jugador,
            new Color(54, 92, 126));
        DrawZazaModuleButton(sb, pixel, panel, ref y, mouse, bw, h, gap,
            "Inventario / objetos", "Editor de objetos", Tab.EditorItems,
            new Color(65, 95, 75));
        DrawZazaModuleButton(sb, pixel, panel, ref y, mouse, bw, h, gap,
            "WorldEdit", "Selección y edición de mundo", Tab.WorldEdit,
            new Color(125, 82, 48));
        DrawZazaModuleButton(sb, pixel, panel, ref y, mouse, bw, h, gap,
            "Mundo / construcción", "Opciones del mundo", Tab.Mundo,
            new Color(80, 80, 120));
        DrawZazaModuleButton(sb, pixel, panel, ref y, mouse, bw, h, gap,
            "NPC y jefes", "Invocación de jefes", Tab.Jefes,
            new Color(118, 62, 62));
        DrawZazaModuleButton(sb, pixel, panel, ref y, mouse, bw, h, gap,
            "Teletransporte", "Teletransporte y marcadores", Tab.Teleport,
            new Color(65, 100, 115));
        DrawZazaModuleButton(sb, pixel, panel, ref y, mouse, bw, h, gap,
            "Jugadores / administración", "Herramientas de jugadores", Tab.Jugadores,
            new Color(92, 78, 112));
        DrawZazaModuleButton(sb, pixel, panel, ref y, mouse, bw, h, gap,
            "Perfiles / utilidades", "Perfiles y marcadores", Tab.Perfiles,
            new Color(70, 105, 85));

        y += 8;
        DrawSectionHeader(sb, panel, ref y, "Estado de la integración");
        DrawInfoLine(sb, panel, ref y, "Módulos", "Separados en archivos .cs para facilitar pruebas y correcciones.");
        DrawInfoLine(sb, panel, ref y, "HERO's", "Las herramientas existentes se conservan; los botones navegan a ellas.");
        DrawInfoLine(sb, panel, ref y, "UI original", "La UI tModLoader de Zaza requiere adaptar sus dependencias antes de compilarse aquí.");
        y += 6;
        Utils.DrawBorderString(sb,
            "Prueba cada módulo y comparte los errores. La UI original completa aún no está portada.",
            new Vector2(panel.X + Pad, y), new Color(240, 190, 110), 0.64f);
    }

    private static void DrawZazaModuleButton(
        SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse,
        int buttonWidth, int buttonHeight, int gap, string label, string status,
        Tab destination, Color background)
    {
        int x = panel.X + Pad;
        Rectangle left = new Rectangle(x, y, buttonWidth, buttonHeight);
        Rectangle right = new Rectangle(x + buttonWidth + gap, y, buttonWidth, buttonHeight);

        if (DrawButton(sb, pixel, left, label, mouse, background))
        {
            currentTab = destination;
            SetStatus("Módulo abierto: " + status + ".");
        }

        string actionLabel = "Abrir";
        if (DrawButton(sb, pixel, right, actionLabel, mouse, new Color(48, 53, 65)))
        {
            currentTab = destination;
            SetStatus("Módulo abierto: " + status + ".");
        }

        y += buttonHeight + 6;
    }
}
