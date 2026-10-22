# Guía: módulos de Zaza dentro de HERO's Mod

## Estado de esta versión

Esta rama contiene un módulo separado de navegación, `HerosModPanel.ZazaModules.cs`, y conserva las herramientas existentes de HERO's. Los botones llevan a pestañas que ya existen: Jugador, Editor de objetos, WorldEdit, Mundo, Jefes, Teleport, Jugadores y Perfiles.

**No es todavía el port completo del código original de Zaza 1.4.4.9.** Las clases originales de su UI dependen de `Terraria.ZazaMod.Mods`, del sistema de UI de tModLoader y de otras clases de Zaza que deben adaptarse a Unified 1.4.5.8 antes de poder compilarse juntas.

## Estructura recomendada para añadir módulos

Guarda los módulos nuevos en `src/Unified/Terraria/Unified/Features/` dentro de este repositorio o en la carpeta equivalente de tu checkout completo de Terraria Unified. El archivo de entrada de esta integración está en esa ruta:

- `HerosModPanel.cs` — panel principal, pestañas y funciones compartidas.
- `HerosModPanel.ZazaModules.cs` — navegación y entrada a los módulos de Zaza. En esta rama ya está colocado también en `src/Unified/Terraria/Unified/Features/`.
- `HerosZazaPlayerModule.cs` — funciones de jugador.
- `HerosZazaItemsModule.cs` — objetos e inventario.
- `HerosZazaWorldEditModule.cs` — selección, pinceles y operaciones de mundo.
- `HerosZazaNpcModule.cs` — NPC, jefes y buffs.
- `HerosZazaUiAdapter.cs` — adaptación visual de la UI original de Zaza.

Los nombres de los módulos futuros son una propuesta de organización; solo `HerosModPanel.ZazaModules.cs` está implementado en esta rama.

## Cómo crear un módulo nuevo

1. Crea un archivo `.cs` nuevo en la carpeta `Unified/Features` del código de Terraria Unified (en el repositorio de trabajo, la ruta equivalente es `src/Unified/Terraria/Unified/Features/`).
2. Usa el namespace `Terraria.Unified.Features`.
3. Si necesita acceder a funciones privadas de `HerosModPanel`, decláralo como `internal static partial class HerosModPanel` y asegúrate de que todas las partes usen el mismo namespace.
4. Crea un método independiente, por ejemplo `DrawZazaItemsModule(...)`, y llama ese método desde el `switch` de pestañas o desde `DrawZazaModTab`.
5. Compila y corrige errores antes de añadir otro módulo. No copies directamente clases que dependan de `Terraria.ModLoader` sin adaptar sus APIs.

Ejemplo de esqueleto:

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Terraria.Unified.Features;

internal static partial class HerosModPanel
{
    private static void DrawZazaItemsModule(
        SpriteBatch sb, Texture2D pixel, Rectangle panel,
        ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Zaza · objetos");
        // Añade aquí una acción real y verificable por botón.
    }
}
```

## Cómo compilar para pruebas

Desde la raíz de tu checkout completo de Terraria Unified, ejecuta:

```powershell
dotnet build .\Terraria.csproj -c Release --no-incremental -v:minimal
```

La ubicación del proyecto puede variar si tienes el checkout completo de Terraria Unified con sus repositorios hermanos (FNA, ReLogic y BuildTasks). Si MSBuild informa que faltan imports o referencias, compila desde el checkout completo y no solo desde estos archivos sueltos.

## Siguiente etapa para portar la UI original

1. Portar las clases base de UI y elementos que no dependan de tModLoader.
2. Reemplazar las llamadas a `Terraria.ModLoader`, hooks y APIs exclusivas de mods por equivalentes de Unified.
3. Portar el sistema de inicialización y ciclo de vida de los `UIState`.
4. Conectar cada botón a una función real, conservando validaciones de mundo/servidor.
5. Compilar y probar por módulos; no activar funciones de administración en multijugador sin comprobar el comportamiento del servidor.

## Estado de compilación comprobable

La ejecución automática de GitHub Actions de esta rama no llegó a compilar el código: falló en el paso Restore porque el workflow intenta restaurar `Terraria-zMod.csproj`, archivo que no existe en la raíz de esta rama. Por tanto, no debe interpretarse como un error C# del módulo ni como una compilación correcta. Para validar los cambios, usa el checkout completo de Unified y el comando anterior.