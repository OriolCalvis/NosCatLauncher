# Pruebas en Windows: traspaso para la sesión del PC

Este documento es para la sesión de Claude Code que se abra en el PC con Windows
(carpeta `C:\Games\n0'sCat`). Resume el estado y qué hay que probar.

## Estado
- **Servidor NosCore** corriendo en el Mac del usuario (Apple Silicon), IP de la red local `192.168.3.10`:
  | Servicio | Dirección |
  |---|---|
  | Login server | `192.168.3.10:4000` |
  | World server (canal `S1-NosCore`) | `192.168.3.10:1337` |
  | NosCore.WebApi (auth) | `http://192.168.3.10:5100` |
  | Master | `192.168.3.10:5050` (solo entre servidores) |
- `world.yml` anuncia `DisplayHost: 192.168.3.10`. Base de datos con 777 mapas, 7698 objetos y 11164 monstruos.
- Cuenta de pruebas: `admin` (GameMaster). La contraseña por defecto está en
  `NosCore/src/NosCore.Parser/ImportFactory.cs:40` del Mac; el usuario la conoce.
- El firewall de macOS está activo: la primera conexión desde el PC puede necesitar
  que el usuario acepte conexiones entrantes para `dotnet` en el Mac.
- **Este launcher** (`NosCatLauncher`) está escrito, compilado y probado en el Mac
  (20 tests, más auth real contra el servidor). **No se ha ejecutado nunca en Windows ni con un cliente real.**

## Pasos
1. Comprobar red: `Test-NetConnection 192.168.3.10 -Port 4000` (y 1337, 5100). Si falla, es el firewall del Mac.
2. Comprobar la API: `Invoke-WebRequest http://192.168.3.10:5100/api/v1/auth/thin/MfaEnabled?username=admin` debe dar 200.
3. Instalar NosTale con el launcher oficial de Gameforge (lo hace el usuario: requiere su cuenta de Gameforge).
   Cerrar después el launcher de Gameforge (ocupa la pipe `GameforgeClientJSONRPC`).
4. Compilar o descomprimir el launcher:
   - Desde el repo: instalar el SDK de .NET 10, `dotnet test tests\NosCatLauncher.Tests`, luego
     `dotnet publish src\NosCatLauncher -c Release -o dist\NosCatLauncher` y copiar `src\NosCatLauncher\launcher.json` a `dist\NosCatLauncher\`.
5. Abrir `NosCatLauncher.exe`, elegir `NostaleClientX.exe`, entrar con `admin` y pulsar **Jugar**.

## Qué mirar en el registro de la ventana
- `IP x.x.x.x → 192.168.3.10`: el parcheo ha encontrado la IP de Gameforge.
  **Si dice 0 IP**, el formato del exe actual no es el que esperamos (ver abajo).
- `Client asked ClientLibrary.isClientRunning / initSession / queryAuthorizationCode / queryGameAccountName: answered`:
  el cliente habla con la pipe. Si no aparece nada, el cliente no encuentra la pipe o falta `gameforge_client_api.dll`.
- En el Mac, `logs/login.log` debería mostrar el `NoS0577` con el código.

## Riesgos conocidos
- **Formato de la IP en el exe**: `ClientPatcher` busca cadenas Delphi `FF FF FF FF | int32 longitud | IP | 00`
  (método del launcher NosCoreLegend de 2020). Si el cliente de 2026 lo guarda distinto, buscar en el exe con
  `Select-String -Pattern '\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}'` sobre una lectura en Latin1 y ajustar el patcher.
- **Puerto**: el patcher cambia palabras `00 A0..A9 0F 00` (puertos 4000–4009) a 4000.
- **`isClientRunning`**: respondemos el texto `"true"` (como GflessClient); si el cliente lo rechaza, probar el booleano `true`
  en `src/NosCatLauncher.Core/GameforgeRpc.cs`.
- **Escritura en la carpeta del juego**: si está en `Program Files`, el launcher necesita ejecutarse como administrador.

## Resultados en Windows (8–11 nov 2026)
Cliente usado: NosTale de **Steam**, copiado a `C:\Games\NosTale-NosCat` (sin Gameforge Client instalado).

Funciona:
- Red y API: 4000, 1337 y 5100 accesibles; `MfaEnabled` → 200.
- Parche: `IP 79.110.84.75 → 192.168.3.10`. Los 9 puertos ya eran 4000, así que "0 ports canviats" es correcto.
- Pipe y autenticación: el cliente pide `isClientRunning`, `initSession`, `isClientRunning`, `queryAuthorizationCode` y `queryGameAccountName`, y recibe el código y `admin`.

Arreglado en el launcher:
- **Bloqueo de la pipe en Windows**: `NamedPipeServerStream` con búferes de 0 bytes hace que cada escritura espere al lector (bloqueo mutuo). Ahora usa 64 KB.
- **Elevación**: `NostaleClientX.exe` es `requireAdministrator` (error 740), así que el launcher también se ejecuta como administrador (`app.manifest`).
- `isClientRunning` responde el booleano `true`; los métodos desconocidos reciben un error JSON-RPC en vez de silencio.
- **Sin Gameforge Client, el `gameforge_api.dll` de Gameforge cierra el cliente**: es un proxy que carga `gameforge_client_api.dll` desde `HKLM\…\Uninstall\{_TNT_CLIENT_APPLICATION_ID o 703bd6d7-79c0-4005-8cd7-89522a05a546}_is1\InstallLocation`. Sin esa clave busca `\gameforge_client_api.dll` (la raíz del disco), no la carpeta del juego. Por eso `native/gfapi` es una **sustituta de `gameforge_api.dll`** con los mismos exports (`_gfc_*@N`, firmas sacadas del desensamblado). El launcher la instala junto al cliente, guarda el original como `gameforge_api.gameforge.dll` y lo restaura si se instala el Gameforge Client. La DLL deja un registro en `%TEMP%\noscat_gfclient.log`.
- Publicar: la copia de la DLL va en un target propio, porque la del SDK falla con el apóstrofo de `n0'sCat`.

**Bloqueo actual: login server de NosCore.** El cliente se queda en el fondo, conectado solo a `:4000`. `login.log` en el Mac muestra `Error al descodificar`. Se capturó el `NoS0577` con un servidor falso local: **se descifra bien con el algoritmo clásico** (`(b - 0x0F) ^ 0xC3`), pero su formato es este:

```
NoS0577 <código en hex> "" NONE_CII 00192B75 0\v0.9.3.3259 0 9D7C52B78955CA4E8133950CBBB7F1A2
```

- Campo vacío (dos espacios) después del código.
- `NONE_CII`: el InstallationId que el cliente lee de `SOFTWARE\WOW6432Node\Gameforge4d\GameforgeClient\MainApp\`, que no existe sin Gameforge Client.
- Hex aleatorio en cada intento, región `0`, versión `0.9.3.3259` y el hash `9D7C…F1A2` (siempre el mismo).

Siguiente paso (en el Mac): comparar la definición de `NoS0577` de NosCore con esta línea y hacer que acepte el campo vacío, `NONE_CII` en lugar de un GUID, y esta versión y este hash.

## Qué no hacer
- No usar funciones pensadas para los servidores oficiales (captchas, huellas, proxies, multicuenta).
- No conectar el cliente parcheado a servidores de Gameforge.
