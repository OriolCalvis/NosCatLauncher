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

## Qué no hacer
- No usar funciones pensadas para los servidores oficiales (captchas, huellas, proxies, multicuenta).
- No conectar el cliente parcheado a servidores de Gameforge.
