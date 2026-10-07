# BigPictureTV

Steam Big Picture en la TV, y solo en la TV.

*[Read in English](README.md)*

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-apoy%C3%A1%20el%20proyecto-FFDD00?logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/emarinelli)

Cuando se abre Big Picture, BigPictureTV cambia las pantallas para que Big
Picture, y los juegos y emuladores que abras desde ahí, aparezcan en la TV en
lugar del monitor o repartidos entre los dos. Cuando cerrás Big Picture, vuelve
tu escritorio normal.

Funciona como un ícono chico junto al reloj. Windows 10 y 11, cualquier placa
de video (NVIDIA, AMD, Intel). No hay que instalar nada ni hace falta ser
administrador.

## Descarga

1. Bajá **BigPictureTV.exe** del [último release](https://github.com/emarinelli26/bp_only_tv/releases/latest).
2. Encendé la TV para que Windows la vea y hacé doble clic en el exe.
3. Se abren los ajustes con la pantalla que cree que es la TV. Revisalo y tocá
   **Probar**.

Windows puede avisar que la app es de un editor desconocido, porque no está
firmada. Hacé clic en **Más información → Ejecutar de todas formas**. Cada
release lo compila GitHub Actions a partir del código de este repo, con su
SHA-256 al lado.

## Qué hace

- Pasa a la TV cuando se abre Big Picture y vuelve cuando se cierra (después de
  unos segundos, con un aviso, por si lo volvés a abrir).
- Salir de Big Picture sin cerrarlo (tecla Windows, Alt+Tab) mantiene la TV.
  Solo al cerrarlo vuelve el escritorio, salvo que lo cambies en los ajustes.
- `Ctrl+Alt+F12` (o doble clic en el ícono) cambia entre la TV y el escritorio
  a mano, desde cualquier lado, también con Big Picture abierto.
- `Ctrl+Alt+Shift+F12` siempre vuelve al escritorio.
- **Abrir Big Picture en la TV**, en el menú, cambia primero para que Steam
  arranque directo en la TV.
- Si Windows vuelve a encender las otras pantallas por su cuenta (la TV entró
  en reposo, Win+P), la app se da cuenta y acompaña.
- Al salir de la app siempre vuelve el escritorio.

El ícono es gris en el escritorio y azul en la TV. La app usa el idioma de
Windows (español o inglés).

## Ajustes

Clic derecho en el ícono → **Ajustes…**

- **Cuál pantalla es la TV.** Automático por defecto; **Identificar pantallas**
  muestra un número grande en cada una.
- **Qué hace el cambio:** solo la TV (por defecto), la TV como principal con
  las demás encendidas, o lo mismo en todas las pantallas.
- **Atajo de teclado:** hacé clic en el cuadro y presioná la combinación.
- **Iniciar con Windows.**

**Opciones avanzadas:**

- **Sonido:** pasar también el sonido a la TV, y devolverlo después.
- **Combo del mando** (desactivado por defecto): uno o varios botones juntos
  para cambiar, desde un toque rápido hasta mantenerlos 5 s (1,5 s por
  defecto). Tocá **Grabar**, apretalos y soltalos, por ejemplo los dos sticks
  (`LS+RS`). Evitá Back, Start y Home: los mandos los usan para sus
  propios atajos (un GameSir Nova Lite cambia de modo con Back+Start+LB).
- Que el atajo y el combo del mando también abran Big Picture.
- Si salir de Big Picture sin cerrarlo vuelve al escritorio.
- Cuánto esperar después de cerrar Big Picture, y programas (emuladores) que
  también usan la TV.
- Aviso cuando hay una versión nueva.

## Menú TV

Un toque al botón **Share/View** del mando (o el atajo de teclado) abre el **Menú TV** en la TV, desde cualquier lado: el escritorio, Big Picture o un juego. Tiene mosaicos grandes para YouTube, Crunchyroll, Big Picture y el escritorio. También se abre desde el ícono junto al reloj. Los botones (uno o varios, con un toque o mantenidos hasta 5 s) se pueden cambiar (o apagar) en Ajustes > Opciones avanzadas > Menú TV.

- **En el menú**: cruceta o stick izquierdo para moverte, A para abrir, B para volver.
- **Como en una consola**: volver al menú no cierra la app. Queda abierta detrás, el mosaico dice "Abierta", A vuelve a ella y X la cierra. Puede haber varias abiertas a la vez (por ejemplo música mientras jugás). Si vas al escritorio, se minimizan.
- **En una página**: se abre en Microsoft Edge a pantalla completa, con un perfil propio por mosaico (las sesiones quedan guardadas y no se mezclan con tu Edge). La app controla la página directamente: cruceta = flechas, A = Enter, B = volver, Start = pausa, LB/RB = elemento anterior/siguiente, LT/RT = subir/bajar página.
- **YouTube** usa su interfaz de TV (youtube.com/tv), que ya se maneja con el mando y trae su teclado.
- **Mandos**: Xbox, PlayStation (DualShock 4, DualSense), Switch y otros.

### Desde Big Picture

Agregá `BigPictureTV.exe` como juego que no es de Steam y en sus propiedades poné `--menu` en Opciones de lanzamiento. Al abrirlo desde Big Picture aparece el Menú TV, y Steam lo considera un juego abierto hasta que cerrás el menú. En la configuración del mando de ese acceso directo elegí la plantilla **Gamepad** (no la de escritorio), así Steam no manda teclas además de la app.

### Editar los mosaicos

Están en `%LOCALAPPDATA%\BigPictureTV\settings.json`, en `TvMenuApps`. Cada uno tiene `Name`, `Kind` (`Web`, `Program`, `BigPicture` o `Desktop`), `Target` (dirección o ruta del programa), y opcionales `Arguments`, `UserAgent`, `BackKey` (tecla del botón B, por ejemplo `Esc` o `Alt+Left`), `SearchKey` (botón Y) y `Color` (`#RRGGBB`). Los cambios se ven la próxima vez que abrís el menú.

## ¿Quedaste atrapado en la TV?

Apretá `Ctrl+Alt+Shift+F12`. Si la app no está abierta, apretá `Win+P` y elegí
**Extender**. Además, la app restaura el diseño pendiente la próxima vez que
arranca.

## Preguntas frecuentes

**¿Manda algo por internet?** Solo el chequeo opcional de versión nueva, que le
pregunta a GitHub por el último release una vez por día. Sin telemetría. Nunca
descarga ni instala nada por su cuenta.

**¿Dónde guarda sus archivos?** En `%LOCALAPPDATA%\BigPictureTV`: ajustes, el
diseño de pantallas guardado mientras estás en la TV, y un registro.

**¿Cómo la desinstalo?** Destildá **Iniciar con Windows**, salí desde el menú y
borrá el exe y la carpeta `%LOCALAPPDATA%\BigPictureTV`.

**No detecta Big Picture.** Busca ventanas con el título *Steam Big Picture
Mode* o *Steam Big Picture*. Si en tu idioma de Steam el título es otro,
agregalo en `BigPictureTitles` dentro de `settings.json` y abrí un issue para
sumarlo.

**Algo salió mal.** Clic derecho en el ícono → **Copiar info de diagnóstico**,
y pegala en un [issue nuevo](https://github.com/emarinelli26/bp_only_tv/issues/new/choose).

## Otras formas de usarla

**Script de PowerShell** (la versión original, sin exe):

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1          # elegís la TV, corre en cada inicio de sesión
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Uninstall
.\BigPictureTV.ps1 -Restore                                     # vuelve el diseño guardado
```

Usá el script o la app, no los dos a la vez.

**Línea de comandos** (`bptv.exe`, en cada release):

```powershell
bptv list        # pantallas, y cuál cree que es la TV
bptv select 2    # elegir la TV a mano
bptv tv-only     # pasar a la TV ahora
bptv restore     # volver al escritorio
bptv watch       # cambiar solo mientras Big Picture está abierto
```

## Compilar

Con el SDK de .NET 8:

```powershell
dotnet test
dotnet publish src/BigPictureTV.App -c Release -r win-x64 -o publish
```

Cada push compila los dos exe en GitHub Actions. Al subir un tag como `v1.0.0`
se crea un borrador de release con ellos.

## Licencia

[MIT](LICENSE)
