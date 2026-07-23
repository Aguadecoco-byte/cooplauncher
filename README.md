# Coop Launcher

Coop Launcher is a Windows launcher that uses a Steam game with **Remote Play Together** support as a safe host for local multiplayer games, emulators, and other applications.

This fork keeps the original idea by [Matheus Baliver](https://github.com/MBaliver/cooplauncher) and adds a corrected Steam overlay, safer donor installation, external shortcuts, tracked relaunching of open applications, diagnostics, and a proper Windows installer.

![Coop Launcher interface](coop.png)

## Cambios de esta versión respecto al proyecto original

Esta edición es una personalización mantenida por [Aguadecoco-byte](https://github.com/Aguadecoco-byte), basada en el proyecto original de [Matheus Baliver](https://github.com/MBaliver/cooplauncher). Conserva su idea principal —usar un juego compatible como donante para Steam Remote Play Together— y añade los siguientes ajustes:

| Área | Proyecto original | Ajuste incorporado en esta versión |
| --- | --- | --- |
| Interfaz de Steam | El overlay de **Shift+Tab** podía aparecer recortado, deformado o superpuesto al launcher. | Superficie de renderizado acelerada a pantalla completa y compatible con el overlay, para mostrar correctamente Remote Play Together y los controles de los jugadores. |
| Segundo jugador | La interfaz defectuosa podía impedir ver o asignar el mando del invitado. | El panel completo de Steam queda visible para asignar los controles del segundo jugador. |
| Juegos externos | La lista dependía principalmente de juegos detectados en Steam. | Permite añadir accesos directos del escritorio y archivos `.lnk`, `.exe`, `.bat`, `.cmd`, `.com` y `.url`, con argumentos, carpeta de trabajo e icono. |
| Permisos | No había una opción individual para elevar aplicaciones. | Cada entrada externa puede configurarse para ejecutarse como administrador, mostrando la advertencia correspondiente sobre el aislamiento de entrada de Windows. |
| Aplicaciones abiertas | No existía un flujo fiable para compartir una aplicación que ya estaba ejecutándose. | Selector integrado de ventanas y cierre controlado para volver a abrir la aplicación como proceso hijo del donante, de modo que Steam pueda asociarla a la sesión. |
| Escritorio y ventanas | Se podía interpretar que Steam permitiría controlar cualquier ventana o todo el escritorio. | La interfaz explica el límite real de Remote Play Together y evita ofrecer una función engañosa: Steam desactiva la entrada remota fuera del árbol de procesos del juego. |
| Instalación en el donante | Reemplazo de archivos con pocas protecciones ante errores. | Copia de seguridad transaccional, comprobación SHA-256, restauración automática si falla la instalación y opción para recuperar el juego original. |
| Configuración y fallos | Persistencia y diagnóstico básicos. | Guardado atómico, recuperación de configuración dañada, registros locales, captura de fallos y mejor detección de bibliotecas de Steam. |
| Pantallas modernas | Problemas de escala y distribución en ciertas resoluciones. | Compatibilidad DPI por monitor y diseño horizontal adaptado al overlay de Steam. |
| Distribución | Compilación manual desde el código fuente. | Instalador `Setup.exe`, accesos directos en escritorio y menú Inicio, desinstalador, pruebas automáticas y flujo de publicación de Releases. |

La implementación también eliminó la credencial de SteamGridDB que estaba incluida en el código. Quien quiera imágenes en línea debe proporcionar su propia variable `STEAMGRIDDB_API_KEY`.

La [comparación completa con el repositorio original](https://github.com/MBaliver/cooplauncher/compare/main...Aguadecoco-byte:cooplauncher:main) permite revisar cada archivo y línea modificada.

## Download

Download the latest `CoopLauncher-Setup-<version>.exe` from the [Releases page](https://github.com/Aguadecoco-byte/cooplauncher/releases/latest).

The installer:

- installs the self-contained 64-bit application for the current Windows user;
- creates Start Menu and desktop shortcuts;
- includes an uninstaller;
- launches Coop Launcher after setup so a Steam donor can be configured.

The community build is not digitally signed, so Windows may show an unknown-publisher warning.

## Features

- Correct full-screen Steam overlay rendering at 60 FPS, including the Remote Play controller slots.
- Automatic Steam library discovery and local/online artwork.
- Transactional donor installation with SHA-256 validation, rollback, and safe restoration.
- External `.lnk`, `.exe`, `.bat`, `.cmd`, `.com`, and `.url` entries.
- Optional per-entry administrator mode with a warning about Windows input isolation.
- Automatic repair of stale per-user `RUNASADMIN` compatibility rules when an external game is configured to run without elevation.
- **Open app** workflow that gracefully closes and relaunches an existing application as a direct child of the Steam donor.
- **Guest mode** that safely closes the donor session before accepting another host's fresh Remote Play Together invitation.
- Atomic configuration saves, corrupt-config recovery, and local diagnostic logs.
- Per-monitor DPI support and a landscape interface suitable for the Steam overlay.
- No embedded SteamGridDB API credential. Set `STEAMGRIDDB_API_KEY` yourself if desired.

## Quick start

1. Install Coop Launcher and open it from its desktop shortcut.
2. Select **Donor**, choose a Steam game with Remote Play Together support, and install the launcher into it.
3. Close the standalone launcher and start the donor game from Steam.
4. Launch a game from Coop Launcher.
5. Press **Shift+Tab**, invite the guest through Remote Play Together, and assign their controller.

For a program that is already open, select **Open app**, save your work, confirm the normal restart, and choose **Close and open inside Steam**. Apps already listed in Coop Launcher should simply be launched from the list.

To join another person's game, close any game launched through Coop Launcher, press **Guest**, and wait until Steam no longer shows the donor as running. Then accept a new Remote Play Together invitation. Coop Launcher deliberately does not force-close games because that could lose unsaved data.

### Remote desktop limitation

Remote Play Together is scoped to the game processes associated with the donor AppID. When the host focuses the Windows desktop or an unrelated pre-existing process, Steam intentionally changes to `Desktop Placeholder` and disables guest input with “Host busy”. The public `ISteamRemotePlay` API does not expose a way to select an arbitrary window, attach an existing PID, or override that protection.

Use Steam Link, Sunshine/Moonlight, or another remote-desktop product when full interactive desktop access is required. Coop Launcher does not bypass Steam's privacy boundary or inject its overlay into unrelated processes.

## Building from source

Requirements:

- Windows 10 or Windows 11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer

```powershell
dotnet build RemotePlayLauncher.csproj -c Release
dotnet run --project tests/SmokeTests/SmokeTests.csproj -c Release
dotnet publish RemotePlayLauncher.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/CoopLauncher.iss
```

The main project produces a self-contained single executable. Installer output is written to `artifacts/installer`.

## Diagnostics and recovery

- Logs: `%LOCALAPPDATA%\CoopLauncher\logs`
- Configuration: `%LOCALAPPDATA%\CoopLauncher\launcher_config.json`
- Restore a donor: open the desktop shortcut, choose **Donor**, then **Restore original game**.

Do not change or restore a donor while it is running.

## Credits

- Original project and design: [Matheus Baliver (@MBaliver)](https://github.com/MBaliver)
- Maintained fork and 1.1.x fixes: [Aguadecoco-byte](https://github.com/Aguadecoco-byte)

## License

Distributed under the [GNU General Public License v3.0](LICENSE), matching the original project. Source code for every published release is available from GitHub.
