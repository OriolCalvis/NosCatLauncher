# NosCat Launcher

Launcher per a Windows que connecta el client oficial de NosTale al servidor NosCore del Mac.

## Què fa
1. Inicia sessió a NosCore.WebApi (`/api/v1/auth/sessions`) amb el teu usuari i contrasenya.
2. Crea `NosCatClient.exe`, una còpia de `NostaleClientX.exe` amb la IP i el port del login canviats.
   L'original no es toca.
3. Obre la pipe `\\.\pipe\GameforgeClientJSONRPC` i hi respon com ho faria el launcher de Gameforge.
   Cada vegada que el client demana el codi, en demana un de nou a `/api/v1/auth/thin/codes`.
4. Arrenca `NosCatClient.exe gf <idioma>`.

## Ús al PC
1. Instal·la NosTale amb el launcher oficial de Gameforge i tanca el launcher de Gameforge.
2. Descomprimeix `dist/NosCatLauncher.zip` i obre `NosCatLauncher.exe`. No cal instal·lar .NET.
3. Tria `NostaleClientX.exe` amb el botó «…», escriu l'usuari i la contrasenya i prem **Jugar**.
4. Deixa la finestra oberta mentre jugues: el client hi parla.

Si la carpeta del joc és a `Program Files`, potser cal obrir el launcher com a administrador
perquè pugui escriure `NosCatClient.exe`.

## launcher.json
| Camp | Valor |
|---|---|
| `AuthUrl` | URL de NosCore.WebApi, ara `http://192.168.3.10:5100` |
| `LoginServerIp` / `LoginServerPort` | Login server, ara `192.168.3.10:4000` |
| `PatchPort` | Canvia també els ports 4000–4009 de l'exe |
| `ClientPath`, `Language`, `LastUser` | Es desen sols des de la finestra |

## Compilar (des del Mac)
```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/NosCatLauncher.Tests
dotnet publish src/NosCatLauncher -c Release -o dist/NosCatLauncher
```
