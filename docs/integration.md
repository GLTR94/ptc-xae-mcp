# Pneutronic XAE MCP — Integration Guide

> Der MCP-Server läuft lokal auf Port 5010 und muss auf derselben Maschine wie TwinCAT
> gestartet sein, bevor ein AI-Client verbunden werden kann.

---

## 1. Claude Code (CLI, ohne IDE)

Für Anwender, die keinen Editor benötigen — nur das Terminal.

### Voraussetzungen
- Node.js 18+ installiert: https://nodejs.org
- Anthropic-Account: https://console.anthropic.com

### Installation

```bash
npm install -g @anthropic-ai/claude-code
```

### Konfiguration

Lege im Verzeichnis, aus dem du Claude Code startest, eine `.mcp.json` an:

```json
{
  "mcpServers": {
    "ptc-xae": {
      "url": "http://localhost:5010/"
    }
  }
}
```

Claude Code liest diese Datei automatisch, wenn es aus diesem Verzeichnis gestartet wird.

### MCP-Server starten

Nach Installation über `ptc-xae-mcp-setup-1.0.exe` liegt die Anwendung standardmässig unter:

```batch
cd "C:\Program Files\Pneutronic\ptc-xae-mcp"
ptc-xae-mcp.exe --http-server
```

> Nur für Entwicklungs-Builds aus dem Repo (ohne Installer) gilt stattdessen:
> `<repo>\src\PtcXaeMcp.WinForms\bin\Release\net10.0-windows\ptc-xae-mcp.exe`

Das Fenster bleibt offen — MCP-Server läuft auf `http://localhost:5010/`.

### Claude Code starten

```bash
cd <repo>
claude
```

Verbindung prüfen:
```
> Prüfe den TwinCAT-Status
```

Claude ruft automatisch `twincat_get_status` auf und antwortet mit dem aktuellen Zustand.

### Autostart (optional)

MCP-Server beim Windows-Start automatisch starten:
1. `Win+R` → `shell:startup`
2. Verknüpfung auf `ptc-xae-mcp.exe --http-server` erstellen

### Claude Code direkt in der TwinCAT XAE Shell (eigenes Terminal-Profil)

Da die TwinCAT XAE Shell auf Visual Studio basiert, lässt sich Claude Code über ein
eigenes Terminal-Profil mit einem Klick direkt aus der IDE heraus starten:

1. In der XAE Shell: **Extras → Optionen → Umgebung → Terminal**
2. Unter **Profile** auf **Hinzufügen** klicken
3. Neues Profil konfigurieren:

   | Feld | Wert |
   |------|------|
   | Name | `Pneutronic&Claude` |
   | Shell-Speicherort | `C:\WINDOWS\system32\cmd.exe` |
   | Argumente | `/k "cd /d <Pfad-zum-TwinCAT-Projekt> && claude"` |

   — Pfad auf das jeweilige TwinCAT-Projektverzeichnis anpassen.
4. **Anwenden** → **OK**
5. MCP-Server starten (falls noch nicht gestartet, siehe oben)
6. Im Terminalfenster der XAE Shell über das Dropdown neben `+` das Profil
   **Pneutronic&Claude** auswählen — Claude Code startet automatisch im
   richtigen Verzeichnis

Damit stehen alle `twincat_*`-Tools direkt im selben Fenster wie das TwinCAT-Projekt
zur Verfügung — ohne Wechsel zwischen IDE und externem Terminal.

---

## 2. GitHub Copilot in Visual Studio / TwinCAT XAE Shell

TwinCAT XAE basiert auf Visual Studio 2022. Ab VS 2022 17.13 unterstützt
GitHub Copilot Chat lokale MCP-Server.

### Voraussetzungen
- GitHub Copilot Lizenz (Individual, Business oder Enterprise)
- Visual Studio 2022 17.13 oder neuer
- GitHub Copilot Extension in VS installiert

### Konfiguration

1. In Visual Studio: **Tools → Options → GitHub → Copilot → MCP Servers**
2. **Add** klicken
3. Eintragen:

| Feld | Wert |
|------|------|
| Name | `ptc-xae` |
| URL  | `http://localhost:5010/` |
| Type | `HTTP` |

4. **OK** → Visual Studio neu starten

### Verwenden

1. **View → GitHub Copilot Chat** öffnen (oder `Ctrl+\, Ctrl+C`)
2. MCP-Server starten (siehe oben)
3. Im Chat-Fenster mit `#` die verfügbaren Tools anzeigen:

```
#twincat_get_status
```

Oder direkt in natürlicher Sprache:

```
Baue das aktuelle PLC-Projekt und zeige mir die Fehler
```

Copilot ruft automatisch `twincat_build_plc` auf und gibt das Ergebnis aus.

### In TwinCAT XAE Shell (ohne vollständiges VS)

Die TwinCAT XAE Shell enthält die Copilot Extension nur, wenn sie manuell installiert
wurde. Vorgehen:

1. Visual Studio Installer öffnen
2. TwinCAT XAE Shell → **Modify**
3. Unter **Individual Components**: `GitHub Copilot` anhaken
4. Installieren

Danach identische Konfiguration wie oben.

---

## 3. GitHub Copilot in VS Code

### Konfiguration

`Ctrl+Shift+P` → **Open User Settings (JSON)** → ergänzen:

```json
"mcp": {
  "servers": {
    "ptc-xae": {
      "url": "http://localhost:5010/"
    }
  }
}
```

Alternativ projektlokal in `.vscode/mcp.json`:

```json
{
  "servers": {
    "ptc-xae": {
      "url": "http://localhost:5010/"
    }
  }
}
```

### Verwenden

Copilot Chat öffnen (`Ctrl+Alt+I`) → Agent Mode aktivieren (Dropdown oben im Chat) →
Tools werden automatisch erkannt. Beispiel:

```
@workspace Aktiviere die TwinCAT-Konfiguration und logge dich in die PLC ein
```

---

## Gemeinsame Hinweise

### MCP-Server prüfen

Im Browser oder mit curl:
```
http://localhost:5010/
```
Antwort `{"status":"ok"}` = Server läuft.

### Firewall

Falls der MCP-Server von anderen Maschinen im Netzwerk erreichbar sein soll
(nicht empfohlen), muss Port 5010 in der Windows-Firewall freigegeben werden.
Standardmässig ist der Server nur auf `localhost` erreichbar.
