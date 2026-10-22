<# 
Apply-PtcXaeMcp.ps1
- Reads schema.json
- Creates EtherCAT tree + EK1122 branching
- Creates/overwrites GVL IO_Imported
- Links PLC vars to IO channels
- Exports .xti + mapping CSV

Run:
  .\Apply-PtcXaeMcp.ps1 -SchemaJson .\schema.json
#>

[CmdletBinding()]
param(
  [string]$SchemaJson = ".\\schema.json",

  [switch]$NoHw,
  [switch]$NoGvl,
  [switch]$NoLink,
  [switch]$NoExport,
  [switch]$AutoConfirmBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$script:ScriptBuild = "2026-03-04-001"
$script:__TcSysMan = $null
$script:__TcDte = $null
$script:__CreatedTreeItemsByName = @{}
$script:__LastGvlItem = $null
$script:__LastGvlPath = ""
$script:__SchemaTerminalPaths = @{}
$script:__GvlDeclVarCache = @{}
$script:__PlcVarPathCache = @{}
$script:__PlcTaskBasePathByDir = @{}

# -------------------- Output helpers --------------------
function Write-Info([string]$Message) {
  Write-Host ("[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message) -ForegroundColor Cyan
}
function Write-Ok([string]$Message) {
  Write-Host ("[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message) -ForegroundColor Green
}
function Write-ErrLine([string]$Message) {
  Write-Host ("[{0}] ERROR: {1}" -f (Get-Date -Format "HH:mm:ss"), $Message) -ForegroundColor Red
}
function Write-Hint([string]$Message) {
  Write-Host ("[{0}] HINWEIS: {1}" -f (Get-Date -Format "HH:mm:ss"), $Message) -ForegroundColor DarkYellow
}
function Write-Step([string]$Message) {
  Write-Host ("[{0}] --- {1} ---" -f (Get-Date -Format "HH:mm:ss"), $Message) -ForegroundColor Yellow
}

function Confirm-YesNo([string]$Prompt, [switch]$DefaultNo, [switch]$AutoYes) {
  if ($AutoYes) {
    Write-Info ("{0} -> Ja (-AutoConfirmBuild)." -f $Prompt)
    return $true
  }

  $suffix = if ($DefaultNo) { "[J/N, Enter=N]" } else { "[J/N, Enter=J]" }
  while ($true) {
    $raw = ""
    try { $raw = [string](Read-Host ("{0} {1}" -f $Prompt, $suffix)) } catch {}
    $answer = $raw.Trim().ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($answer)) { return (-not $DefaultNo) }
    if (@("J","JA","Y","YES") -contains $answer) { return $true }
    if (@("N","NEIN","NO") -contains $answer) { return $false }
    Write-Warning "Ungueltige Eingabe. Bitte J oder N eingeben."
  }
}

function Show-RunConfiguration([string]$SchemaPath, [bool]$SkipHw, [bool]$SkipGvl, [bool]$SkipLink, [bool]$SkipExport, [bool]$AutoBuildConfirm) {
  Write-Step "Konfiguration"
  Write-Info ("Schema-JSON       : {0}" -f $SchemaPath)
  Write-Info ("Hardware aufbauen : {0}" -f ($(if ($SkipHw) { "Nein (skip)" } else { "Ja" })))
  Write-Info ("GVL schreiben     : {0}" -f ($(if ($SkipGvl) { "Nein (skip)" } else { "Ja" })))
  Write-Info ("Links erstellen   : {0}" -f ($(if ($SkipLink) { "Nein (skip)" } else { "Ja" })))
  Write-Info ("Export schreiben  : {0}" -f ($(if ($SkipExport) { "Nein (skip)" } else { "Ja" })))
  Write-Info ("Build auto-bestaetigen: {0}" -f ($(if ($AutoBuildConfirm) { "Ja" } else { "Nein" })))
}

function ConvertFrom-JsonCompat([string]$Json, [int]$Depth = 100) {
  $command = Get-Command ConvertFrom-Json -ErrorAction Stop
  if ($command.Parameters.ContainsKey('Depth')) {
    return $Json | ConvertFrom-Json -Depth $Depth
  }
  return $Json | ConvertFrom-Json
}

function Validate-SchemaConfig($cfg) {
  $issues = New-Object System.Collections.Generic.List[string]
  if ($null -eq $cfg) { $issues.Add("Schema ist leer.") | Out-Null }
  if ($null -eq $cfg.twincat) {
    $issues.Add("Root 'twincat' fehlt.") | Out-Null
  } else {
    if ([string]::IsNullOrWhiteSpace([string]$cfg.twincat.deviceName)) { $issues.Add("twincat.deviceName fehlt oder ist leer.") | Out-Null }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.twincat.plcProjectName)) { $issues.Add("twincat.plcProjectName fehlt oder ist leer.") | Out-Null }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.twincat.gvlName)) { $issues.Add("twincat.gvlName fehlt oder ist leer.") | Out-Null }
  }
  if ($null -eq $cfg.ethercat -or $null -eq $cfg.ethercat.root) {
    $issues.Add("ethercat.root fehlt.") | Out-Null
  }
  if ($null -eq $cfg.ioPoints) {
    $issues.Add("ioPoints fehlt.") | Out-Null
  } elseif (@($cfg.ioPoints).Count -eq 0) {
    $issues.Add("ioPoints ist leer.") | Out-Null
  }

  $idx = 0
  foreach ($p in @($cfg.ioPoints)) {
    $idx++
    if ([string]::IsNullOrWhiteSpace([string]$p.varName)) { $issues.Add(("ioPoints[{0}].varName fehlt." -f $idx)) | Out-Null }
    if ([string]::IsNullOrWhiteSpace([string]$p.terminalName)) { $issues.Add(("ioPoints[{0}].terminalName fehlt." -f $idx)) | Out-Null }
    $chParsed = 0
    $hasValidChannel = $false
    if ($null -ne $p.channel) {
      $hasValidChannel = [int]::TryParse([string]$p.channel, [ref]$chParsed)
    }
    if (-not $hasValidChannel) {
      $issues.Add(("ioPoints[{0}].channel ist keine gueltige Zahl." -f $idx)) | Out-Null
    } else {
      if ($chParsed -lt 1) { $issues.Add(("ioPoints[{0}].channel muss >= 1 sein." -f $idx)) | Out-Null }
    }
    $dir = ([string]$p.direction).ToUpperInvariant().Trim()
    if ($dir -ne "I" -and $dir -ne "Q") { $issues.Add(("ioPoints[{0}].direction muss 'I' oder 'Q' sein." -f $idx)) | Out-Null }
  }

  if ($issues.Count -gt 0) {
    $msg = "Ungueltige Schema-Konfiguration:" + [Environment]::NewLine + " - " + ($issues -join ([Environment]::NewLine + " - "))
    throw $msg
  }
}

function Show-ErrorGuidance($errorRecord, [string]$schemaPath) {
  $msg = ""
  try { $msg = [string]$errorRecord.Exception.Message } catch {}
  $m = $msg.ToLowerInvariant()

  if ($m -match "keine laufende twincat xae instanz gefunden") {
    Write-Hint "TwinCAT XAE starten und die richtige Solution oeffnen."
    Write-Hint "PowerShell und TwinCAT mit gleicher Berechtigungsstufe starten (beide normal oder beide als Admin)."
    return
  }
  if ($m -match "kein twincat project \(system manager\)") {
    Write-Hint "In der geoeffneten Solution muss ein TwinCAT-Projekt enthalten sein."
    Write-Hint "Pruefen, ob die richtige .sln geladen ist."
    return
  }
  if ($m -match "ethercat-master muss vorkonfiguriert|geraet unter tiid nicht gefunden|tiid\^") {
    Write-Hint "Im I/O-Baum muss der Master bereits vorhanden sein (Name aus twincat.deviceName)."
    Write-Hint "Master in TwinCAT manuell anlegen/benennen und Script erneut starten."
    return
  }
  if ($m -match "kompilierfehler|build wurde nicht bestaetigt|plc build/aktualisierung fehlgeschlagen") {
    Write-Hint "Kompilierung in TwinCAT/Visual Studio pruefen (Fehlerliste)."
    Write-Hint "Erst bei fehlerfreiem Build erneut verlinken/exportieren."
    return
  }
  if ($m -match "json") {
    Write-Hint ("Schema-Datei pruefen: {0}" -f $schemaPath)
    Write-Hint "Auf gueltige JSON-Syntax und Pflichtfelder achten (twincat.*, ethercat.root, ioPoints[*])."
    return
  }
}


# -------------------- TwinCAT attach helpers --------------------

function Get-ActiveComObject([string]$progId) {
  # Windows PowerShell 5.1 hat Marshal.GetActiveObject, in pwsh fehlt es je nach Runtime.
  $marshalType = [Runtime.InteropServices.Marshal]
  $hasMarshalGetActiveObject = $marshalType.GetMethods() | Where-Object {
    $_.Name -eq "GetActiveObject" -and $_.GetParameters().Count -eq 1
  }
  if ($hasMarshalGetActiveObject) {
    return $marshalType::GetActiveObject($progId)
  }

  if (-not ("ComInteropHelper" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ComInteropHelper
{
  [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
  private static extern void CLSIDFromProgIDEx(string progId, out Guid clsid);

  [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
  private static extern void CLSIDFromProgID(string progId, out Guid clsid);

  [DllImport("oleaut32.dll", PreserveSig = false)]
  private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

  public static object GetActiveObjectByProgId(string progId)
  {
    Guid clsid;
    try { CLSIDFromProgIDEx(progId, out clsid); }
    catch { CLSIDFromProgID(progId, out clsid); }
    object obj;
    GetActiveObject(ref clsid, IntPtr.Zero, out obj);
    return obj;
  }
}
"@
  }

  return [ComInteropHelper]::GetActiveObjectByProgId($progId)
}

function Get-ActiveTcDte {
  $progIds = @(
    "TcXaeShell.DTE.17.0", # VS2022
    "TcXaeShell.DTE.16.0", # VS2019
    "TcXaeShell.DTE.15.0"  # VS2017
  )

  # Ergänze dynamisch erkannte TwinCAT ProgIDs aus HKCR, damit auch neuere/abweichende Versionen gefunden werden.
  try {
    $dynamic = Get-ChildItem -Path Registry::HKEY_CLASSES_ROOT -ErrorAction Stop |
      Where-Object { $_.PSChildName -match '^TcXaeShell\.DTE\.\d+\.\d+$' } |
      ForEach-Object { $_.PSChildName } |
      Sort-Object {
        try { [version](($_ -replace '^TcXaeShell\.DTE\.','') + '.0') } catch { [version]'0.0.0' }
      } -Descending
    if ($dynamic) { $progIds = @($dynamic + $progIds | Select-Object -Unique) }
  } catch {}

  foreach ($p in $progIds) {
    try {
      $obj = Get-ActiveComObject -progId $p
      if ($null -ne $obj) { return $obj }
    } catch {}
  }

  throw ("Keine laufende TwinCAT XAE Instanz gefunden ({0}). " +
         "TwinCAT XAE starten & Solution öffnen. " +
         "Falls XAE bereits läuft: PowerShell und XAE mit gleicher Benutzer-/Admin-Elevation starten." -f
         (($progIds -join ", ")))
}

function Find-DirectChildByNameSafe($parent, [string]$name) {
  if ($null -eq $parent) { return $null }
  try { $cnt = [int]$parent.ChildCount } catch { $cnt = 0 }
  for ($i = $cnt; $i -ge 1; $i--) {
    $ch = Get-ChildAtSafe -parent $parent -index $i
    if ($null -eq $ch) { continue }
    if ((Get-ItemNameSafe $ch) -eq $name) { return $ch }
  }
  return $null
}

function Get-ChildAtSafe($parent, [int]$index) {
  if ($null -eq $parent) { return $null }
  if ($index -lt 1) { return $null }

  function Unwrap-SingleItem($value) {
    if ($null -eq $value) { return $null }
    if ($value -is [System.Array]) {
      if ($value.Count -eq 1) { return $value[0] }
      return $null
    }
    return $value
  }

  function Get-ChildWithRetry([int]$idx) {
    $attempts = 0
    while ($attempts -lt 8) {
      $attempts++
      try {
        return Unwrap-SingleItem ($parent.get_Child($idx))
      } catch {
        $m = ""
        try { $m = [string]$_.Exception.Message } catch {}
        $isRejected = ($m -match "0x80010001") -or ($m -match "RPC_E_CALL_REJECTED") -or ($m -match "Aufruf wurde durch Aufgerufenen abgelehnt")
        if (-not $isRejected) { break }
        Start-Sleep -Milliseconds 120
      }
    }
    return $null
  }

  $ch = Get-ChildWithRetry $index
  if ($null -ne $ch) { return $ch }
  if ($index -gt 1) {
    $ch = Get-ChildWithRetry ($index - 1)
    if ($null -ne $ch) { return $ch }
  }
  return $null
}

function Get-ChildCountSafe($item, [int]$fallback = 0) {
  if ($null -eq $item) { return $fallback }
  $attempts = 0
  while ($attempts -lt 8) {
    $attempts++
    try { return [int]$item.ChildCount } catch {
      $m = ""
      try { $m = [string]$_.Exception.Message } catch {}
      $isRejected = ($m -match "0x80010001") -or ($m -match "RPC_E_CALL_REJECTED") -or ($m -match "Aufruf wurde durch Aufgerufenen abgelehnt")
      if (-not $isRejected) { return $fallback }
      Start-Sleep -Milliseconds 120
    }
  }
  return $fallback
}

function Get-NewestDirectChildSafe($parent) {
  if ($null -eq $parent) { return $null }
  try { $cnt = [int]$parent.ChildCount } catch { $cnt = 0 }
  if ($cnt -le 0) { return $null }
  return (Get-ChildAtSafe -parent $parent -index $cnt)
}

function Wait-DirectChildAfterCreate($parent, [string]$name, [int]$beforeCount, [int]$timeoutMs = 1500, [int]$sleepMs = 100) {
  $deadline = (Get-Date).AddMilliseconds($timeoutMs)
  do {
    $byName = Find-DirectChildByNameSafe -parent $parent -name $name
    if ($null -ne $byName) { return $byName }

    $cnt = 0
    try { $cnt = [int]$parent.ChildCount } catch {}
    if ($cnt -gt $beforeCount) {
      $newest = Get-NewestDirectChildSafe -parent $parent
      if ($null -ne $newest) { return $newest }
    }

    Start-Sleep -Milliseconds $sleepMs
  } while ((Get-Date) -lt $deadline)

  return $null
}

function Resolve-ExpectedChildByPath($parent, [string]$name) {
  if ($null -eq $parent) { return $null }
  if ($null -eq $script:__TcSysMan) { return $null }
  $ppath = $null
  try { $ppath = [string]$parent.PathName } catch {}
  if ([string]::IsNullOrWhiteSpace($ppath)) { return $null }
  $expectedPath = "{0}^{1}" -f $ppath, $name
  $raw = Lookup-OrNull -sysman $script:__TcSysMan -path $expectedPath
  return (Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath $expectedPath -expectedName $name -expectedSubType -1)
}

function Refresh-ItemByPath($item) {
  if ($null -eq $item) { return $null }
  if ($null -eq $script:__TcSysMan) { return $item }
  $path = $null
  try { $path = [string]$item.PathName } catch {}
  if ([string]::IsNullOrWhiteSpace($path)) { return $item }
  $raw = Lookup-OrNull -sysman $script:__TcSysMan -path $path
  $fresh = Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath $path -expectedName (Get-ItemNameSafe $item) -expectedSubType (Get-ItemSubTypeSafe $item)
  if ($null -ne $fresh) { return $fresh }
  return $item
}

function Wait-ResolveChildAfterCreate($parent, [string]$name, [int]$beforeCount, [int]$timeoutMs = 8000, [int]$sleepMs = 100) {
  $deadline = (Get-Date).AddMilliseconds($timeoutMs)
  do {
    $parentFresh = Refresh-ItemByPath -item $parent

    $byName = Find-DirectChildByNameSafe -parent $parent -name $name
    if ($null -ne $byName) { return $byName }
    $byNameFresh = Find-DirectChildByNameSafe -parent $parentFresh -name $name
    if ($null -ne $byNameFresh) { return $byNameFresh }

    $byPath = Resolve-ExpectedChildByPath -parent $parent -name $name
    if ($null -ne $byPath) { return $byPath }
    $byPathFresh = Resolve-ExpectedChildByPath -parent $parentFresh -name $name
    if ($null -ne $byPathFresh) { return $byPathFresh }

    $cnt = 0
    try { $cnt = [int]$parent.ChildCount } catch {}
    $cntFresh = 0
    try { $cntFresh = [int]$parentFresh.ChildCount } catch {}
    if ($cnt -gt $beforeCount) {
      $newest = Get-NewestDirectChildSafe -parent $parent
      if ($null -ne $newest) { return $newest }
    }
    if ($cntFresh -gt $beforeCount) {
      $newestFresh = Get-NewestDirectChildSafe -parent $parentFresh
      if ($null -ne $newestFresh) { return $newestFresh }
    }

    Start-Sleep -Milliseconds $sleepMs
  } while ((Get-Date) -lt $deadline)

  return $null
}

function Test-IsNullLike($value) {
  if ($null -eq $value) { return $true }
  if ($value -is [System.Management.Automation.Internal.AutomationNull]) { return $true }
  return $false
}

function Ensure-SingleTreeItem($item, [string]$context = "item") {
  if ($null -eq $item) { return $null }
  if ($item -is [System.Array]) {
    if ($item.Count -eq 1) { return $item[0] }
    throw ("Ungueltiges COM-TreeItem in {0}: Object[] (Count={1}) statt einzelnes TreeItem erhalten." -f $context, $item.Count)
  }
  return $item
}

function Resolve-EthercatMasterItem($sysman, $ioRoot, [string]$deviceName, $candidate) {
  $masterPath = "TIID^{0}" -f $deviceName

  $byCandidate = Select-SingleTreeItemFromLookup -lookupResult $candidate -expectedPath $masterPath -expectedName $deviceName -expectedSubType 111
  if ($null -ne $byCandidate) { return $byCandidate }

  # Primary: resolve by explicit TIID path
  $byPathRaw = Lookup-OrNull -sysman $sysman -path $masterPath
  $byPath = Select-SingleTreeItemFromLookup -lookupResult $byPathRaw -expectedPath $masterPath -expectedName $deviceName -expectedSubType 111
  if ($null -ne $byPath) { return $byPath }

  # Reliable fallback: enumerate IO root children and match name+subtype
  $byEnum = Find-IoRootChildByNameAndSubtype -ioRoot $ioRoot -name $deviceName -subType 111
  if ($null -ne $byEnum) { return $byEnum }

  throw ("EtherCAT-Master '{0}' konnte nicht als einzelnes TreeItem aufgeloest werden." -f $deviceName)
}

function New-EbusChildSafe($parent, [string]$name, [string]$vInfo) {
  $parent = Ensure-SingleTreeItem -item $parent -context "New-EbusChildSafe(parent)"
  Write-Info ("CreateChild '{0}' [vInfo={1}] unter '{2}'" -f $name, $vInfo, (Get-ItemNameSafe $parent))

  try {
    if (Test-IsNullLike $parent) {
      throw "Elternelement ist <null> (vor CreateChild)."
    }

    $beforeCount = 0
    try { $beforeCount = [int]$parent.ChildCount } catch {}

    $created = $null
    $createErr = $null
    try {
      $created = $parent.CreateChild($name, 9099, $null, $vInfo)
    } catch {
      $createErr = $_.Exception.Message
    }

    # Fallback: manche XAE-Installationen akzeptieren nur Basis-vInfo ohne Revisionssuffix (z.B. EK1200 statt EK1200-5000).
    if ((Test-IsNullLike $created) -and $vInfo -match '^([A-Za-z]{2}\d{3,4})-[A-Za-z0-9]+$') {
      $baseVInfo = $matches[1]
      Write-Warning ("CreateChild mit vInfo '{0}' fehlgeschlagen; Wiederholung mit Basis-vInfo '{1}'." -f $vInfo, $baseVInfo)
      try {
        $created = $parent.CreateChild($name, 9099, $null, $baseVInfo)
      } catch {}
    }

    # In einigen COM-Kombinationen wird der Knoten erstellt, aber der Rückgabewert ist leer/instabil.
    # Dann Child direkt unter dem Parent per Name auflösen.
    if (Test-IsNullLike $created) {
      $created = Wait-ResolveChildAfterCreate -parent $parent -name $name -beforeCount $beforeCount
    } else {
      $usable = $true
      try { $null = $created.PathName } catch { $usable = $false }
      if (-not $usable) {
        $resolved = Wait-ResolveChildAfterCreate -parent $parent -name $name -beforeCount $beforeCount
        if ($null -ne $resolved) { $created = $resolved }
      }
    }

    if (Test-IsNullLike $created) {
      $pname = Get-ItemNameSafe $parent
      $ppath = ""
      try { $ppath = [string]$parent.PathName } catch {}
      throw ("CreateChild lieferte <null>: parent='{0}' path='{1}' child='{2}' vInfo='{3}'. " -f $pname, $ppath, $name, $vInfo) +
            ("Inner='{0}'. " -f $createErr) +
            "HINT: Gerätebeschreibung/ESI für dieses vInfo fehlt oder vInfo ist nicht exakt passend (z.B. Revision/Variante)."
    }
    return $created
  } catch {
    $pname = $null
    $ppath = $null
    $inner = $null
    try { $pname = Get-ItemNameSafe $parent } catch {}
    try { $ppath = $parent.PathName } catch {}
    try { $inner = $_.Exception.Message } catch {}

    $msg = ("CreateChild fehlgeschlagen: parent='{0}' path='{1}' child='{2}' vInfo='{3}'. " -f `
            $pname, $ppath, $name, $vInfo) +
           ("COM: {0}. " -f $inner) +
           "HINT: Unter EtherCAT-Master nur EKxxxx, keine ELxxxx. vInfo muss exakt sein (z.B. 'EL1809')."
    throw $msg
  }
}

function Assert-AllowedChild([string]$parentVInfo, [string]$childVInfo, [string]$parentName, [string]$childName) {
  # Die reale Topologie wird von TwinCAT validiert; hier keine heuristische Einschränkung.
}

function Build-EthercatNodeRecursiveSafe($parentItem, $parentVInfo, $node) {
  $parentItem = Ensure-SingleTreeItem -item $parentItem -context "Build-EthercatNodeRecursiveSafe(parentItem)"
  Assert-AllowedChild -parentVInfo $parentVInfo -childVInfo $node.vInfo -parentName (Get-ItemNameSafe $parentItem) -childName $node.name

  $parentItem = Refresh-ItemByPath -item $parentItem
  $item = New-EbusChildSafe -parent $parentItem -name $node.name -vInfo $node.vInfo
  if (Test-IsNullLike $item) {
    $item = Find-DirectChildByNameSafe -parent $parentItem -name $node.name
  }
  if (Test-IsNullLike $item) {
    $parentFresh = Refresh-ItemByPath -item $parentItem
    $item = Find-DirectChildByNameSafe -parent $parentFresh -name $node.name
  }
  if (Test-IsNullLike $item) {
    $item = Get-NewestDirectChildSafe -parent $parentItem
    if ($null -ne $item) {
      Write-Warning ("CreateChild fuer '{0}' lieferte null; verwende neuesten Kindknoten '{1}' unter '{2}'." -f `
                     $node.name, (Get-ItemNameSafe $item), (Get-ItemNameSafe $parentItem))
    }
  }
  if (Test-IsNullLike $item) {
    throw ("CreateChild lieferte null-aehnliches Objekt fuer Knoten '{0}' (vInfo='{1}') unter Elternelement '{2}' (ChildCount={3})." -f `
           $node.name, $node.vInfo, (Get-ItemNameSafe $parentItem), ([int]$parentItem.ChildCount))
  }

  # Cache created items for deterministic linking in the same run.
  # Prefer path strings (stable across COM proxy refreshes).
  $itemPath = ""
  try { $itemPath = [string]$item.PathName } catch {}
  $cacheVal = if ([string]::IsNullOrWhiteSpace($itemPath)) { $item } else { $itemPath }
  $script:__CreatedTreeItemsByName[[string]$node.name] = $cacheVal
  $nk = Get-TerminalNameKey ([string]$node.name)
  if (-not [string]::IsNullOrWhiteSpace($nk)) {
    $script:__CreatedTreeItemsByName["__KEY__" + $nk] = $cacheVal
  }

  # PreviousPort NICHT sofort nach CreateChild setzen (robuster),
  # sondern erst nachdem beide Branches existieren -> wir merken uns "pending".
  if ($node.PSObject.Properties.Name -contains "previousPort" -and $node.previousPort) {
    $item | Add-Member -NotePropertyName "__pendingPreviousPort" -NotePropertyValue ([string]$node.previousPort) -Force
  }

  if ($node.PSObject.Properties.Name -contains "children" -and $node.children) {
    foreach ($ch in $node.children) {
      Build-EthercatNodeRecursiveSafe -parentItem $item -parentVInfo $node.vInfo -node $ch
    }
  }
  return $item
}

function Apply-PendingPreviousPorts($rootItem) {
  foreach ($it in Get-TreeItemsDepthFirst $rootItem 200000) {
    if ($it.PSObject.Properties.Name -contains "__pendingPreviousPort") {
      $port = [string]$it.__pendingPreviousPort
      if ($port) { Set-PreviousPort -slaveItem $it -desiredPortLetter $port }
    }
  }
}

function Get-SystemManagerFromSolution($dte) {
  for ($i=1; $i -le $dte.Solution.Projects.Count; $i++) {
    $proj = $dte.Solution.Projects.Item($i)
    try {
      $obj = $proj.Object
      if ($null -ne $obj -and $obj.PSObject.Methods.Name -contains "LookupTreeItem") {
        return $obj
      }
    } catch {}
  }
  throw "Kein TwinCAT Project (System Manager) in der aktuellen Solution gefunden."
}

function Refresh-TwinCatSession {
  $dte = Get-ActiveTcDte
  $sysman = Get-SystemManagerFromSolution $dte
  $script:__TcDte = $dte
  $script:__TcSysMan = $sysman
  return $sysman
}

function Wait-ForVsBuildDone($dte, [int]$timeoutSec = 180) {
  if ($null -eq $dte) { return $true }
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  $sawBuildStart = $false
  while ((Get-Date) -lt $deadline) {
    $state = 1
    try { $state = [int]$dte.Solution.SolutionBuild.BuildState } catch {}
    # EnvDTE.vsBuildState: NotStarted=1, InProgress=2, Done=3
    if ($state -eq 2) { $sawBuildStart = $true }
    if ($state -eq 3) { return $true }
    if ($state -eq 1 -and $sawBuildStart) { return $true }
    Start-Sleep -Milliseconds 300
  }
  Write-Warning "Zeitueberschreitung beim Warten auf den Visual Studio/TwinCAT Build-Status. Es wird fortgesetzt."
  return $false
}

function Refresh-PlcProcessImage($dte) {
  if ($null -eq $dte) { return }
  try {
    Write-Step "PLC Build"
    Write-Info "Aktualisiere PLC Process Image (Build wird gestartet)..."
    $dte.Solution.SolutionBuild.Build($true)
    Wait-ForVsBuildDone -dte $dte -timeoutSec 180 | Out-Null

    $failedProjects = 0
    try { $failedProjects = [int]$dte.Solution.SolutionBuild.LastBuildInfo } catch {}
    if ($failedProjects -gt 0) {
      throw ("Kompilierfehler: Build fehlgeschlagen ({0} Projekt(e) mit Fehlern)." -f $failedProjects)
    }

    $confirmed = Confirm-YesNo -Prompt "Build abgeschlossen und ohne Fehler?" -DefaultNo -AutoYes:$AutoConfirmBuild
    if (-not $confirmed) {
      throw "Abbruch durch Benutzer: PLC Build wurde nicht bestaetigt."
    }
  } catch {
    Write-Warning ("PLC Build/Aktualisierung fehlgeschlagen: {0}" -f $_.Exception.Message)
    throw
  }
}

function Resolve-PlcProjectName($sysman, [string]$requestedName) {
  $tipc = Lookup-OrNull $sysman "TIPC"
  $tipcRoots = @()
  if ($tipc -is [System.Array]) { $tipcRoots = @($tipc) }
  elseif ($null -ne $tipc) { $tipcRoots = @($tipc) }

  $candidates = New-Object System.Collections.Generic.List[string]
  foreach ($r in $tipcRoots) {
    foreach ($it in Get-TreeItemsDepthFirst $r 250000) {
      $nm = Get-ItemNameSafe $it ""
      if ([string]::IsNullOrWhiteSpace($nm)) { continue }
      if ($nm -match "^(.*)\s+Project$" -or $nm -match "^(.*)\s+Projekt$") {
        $baseName = $matches[1].Trim()
        if (-not [string]::IsNullOrWhiteSpace($baseName) -and -not $candidates.Contains($baseName)) {
          $candidates.Add($baseName) | Out-Null
        }
      }
    }
  }

  if (-not [string]::IsNullOrWhiteSpace($requestedName)) {
    foreach ($c in $candidates) {
      if ($c -ieq $requestedName) { return $c }
    }
  }
  if ($candidates.Count -gt 0) { return $candidates[0] }
  return $requestedName
}

function Ensure-OutputFolder([string]$path) {
  if ([string]::IsNullOrWhiteSpace($path)) { return }
  $dir = Split-Path -Parent $path
  if ([string]::IsNullOrWhiteSpace($dir)) { $dir = (Get-Location).Path }
  if (!(Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
}

function Lookup-OrNull($sysman, [string]$path) {
  try { return $sysman.LookupTreeItem($path) } catch { return $null }
}

function Select-SingleTreeItemFromLookup($lookupResult, [string]$expectedPath = "", [string]$expectedName = "", [int]$expectedSubType = -1) {
  function Test-Match($it, [string]$path, [string]$name, [int]$stExpected) {
    if ($null -eq $it) { return $false }
    if ($it -is [System.Array]) { return $false }

    if (-not [string]::IsNullOrWhiteSpace($path)) {
      $itPath = $null
      try { $itPath = [string]$it.PathName } catch {}
      if ($itPath -ne $path) { return $false }
    }
    if (-not [string]::IsNullOrWhiteSpace($name)) {
      if ((Get-ItemNameSafe $it) -ne $name) { return $false }
    }
    if ($stExpected -ge 0) {
      if ((Get-ItemSubTypeSafe $it) -ne $stExpected) { return $false }
    }
    return $true
  }

  if ($null -eq $lookupResult) { return $null }

  if ($lookupResult -isnot [System.Array]) {
    if (Test-Match $lookupResult $expectedPath $expectedName $expectedSubType) { return $lookupResult }
    return $null
  }

  foreach ($it in $lookupResult) {
    if (Test-Match $it $expectedPath $expectedName $expectedSubType) { return $it }
  }
  foreach ($it in $lookupResult) {
    $p = $null
    try { $p = $it.Parent } catch {}
    if (Test-Match $p $expectedPath $expectedName $expectedSubType) { return $p }
  }
  foreach ($it in $lookupResult) {
    if (Test-Match $it "" $expectedName $expectedSubType) { return $it }
  }
  foreach ($it in $lookupResult) {
    $p = $null
    try { $p = $it.Parent } catch {}
    if (Test-Match $p "" $expectedName $expectedSubType) { return $p }
  }

  return $null
}

function Get-ItemSubTypeSafe($item, [int]$fallback = -1) {
  if ($null -eq $item) { return $fallback }
  try { return [int]$item.ItemSubType } catch {}
  try { return [int]$item.SubType } catch {}
  return $fallback
}

function Find-IoRootChildByNameAndSubtype($ioRoot, [string]$name, [int]$subType) {
  if ($null -eq $ioRoot) { return $null }
  try { $cnt = [int]$ioRoot.ChildCount } catch { $cnt = 0 }
  for ($i = 1; $i -le $cnt; $i++) {
    $ch = Get-ChildAtSafe -parent $ioRoot -index $i
    if ($null -eq $ch) { continue }
    $n = Get-ItemNameSafe $ch
    $st = Get-ItemSubTypeSafe $ch
    if ($n -eq $name -and $st -eq $subType) { return $ch }
  }
  return $null
}

function Get-ItemNameSafe($item, [string]$fallback = "<unknown>") {
  if ($null -eq $item) { return $fallback }
  try {
    $n = [string]$item.Name
    if (-not [string]::IsNullOrWhiteSpace($n)) { return $n }
  } catch {}
  try {
    $n = [string]$item.ItemName
    if (-not [string]::IsNullOrWhiteSpace($n)) { return $n }
  } catch {}
  try {
    $p = [string]$item.PathName
    if (-not [string]::IsNullOrWhiteSpace($p)) {
      $parts = $p -split '\^'
      if ($parts.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($parts[-1])) { return [string]$parts[-1] }
    }
  } catch {}
  try { return [string]$item } catch {}
  return $fallback
}

function Clear-Children($parent) {
  try {
    $parent = Refresh-ItemByPath -item $parent
    $maxPasses = 12
    for ($pass = 1; $pass -le $maxPasses; $pass++) {
      $parent = Refresh-ItemByPath -item $parent
      $cnt = Get-ChildCountSafe -item $parent -fallback 0
      if ($cnt -le 0) { return }
      if ($pass -eq 1) {
        Write-Info ("Loesche vorhandene Kinder von '{0}' ({1} Elemente)..." -f (Get-ItemNameSafe $parent), $cnt)
      }
      for ($i=$cnt; $i -ge 1; $i--) {
        $parent = Refresh-ItemByPath -item $parent
        $ch = Get-ChildAtSafe -parent $parent -index $i
        if ($null -eq $ch) { continue }
        Invoke-WithComRetry -Action { $ch.Delete() | Out-Null } -MaxAttempts 12 -SleepMs 250 | Out-Null
        Start-Sleep -Milliseconds 80
      }
      Start-Sleep -Milliseconds 350
    }
    $parent = Refresh-ItemByPath -item $parent
    $rest = Get-ChildCountSafe -item $parent -fallback 0
    if ($rest -gt 0) {
      throw ("Konnte nicht alle Kinder unter '{0}' loeschen (verbleibend: {1})." -f (Get-ItemNameSafe $parent), $rest)
    }
  } catch {
    Write-Warning ("Clear-Children fehlgeschlagen fuer '{0}': {1}" -f (Get-ItemNameSafe $parent), $_.Exception.Message)
  }
}

function Get-DirectChildrenSnapshot($parent, [int]$maxItems = 200) {
  $items = New-Object System.Collections.Generic.List[object]
  if ($null -eq $parent) { return @($items) }
  $parent = Refresh-ItemByPath -item $parent
  $cnt = Get-ChildCountSafe -item $parent -fallback 0
  $limit = [Math]::Min($cnt, $maxItems)
  for ($i = 1; $i -le $limit; $i++) {
    $ch = Get-ChildAtSafe -parent $parent -index $i
    if ($null -eq $ch) { continue }
    $path = ""
    try { $path = [string]$ch.PathName } catch {}
    $items.Add([pscustomobject]@{
      Name = Get-ItemNameSafe $ch
      Path = $path
      SubType = Get-ItemSubTypeSafe $ch
    }) | Out-Null
  }
  return @($items)
}

function Get-SchemaNodeNames($node) {
  $names = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)
  function Add-NamesRecursive($current) {
    if ($null -eq $current) { return }
    $nm = [string]$current.name
    if (-not [string]::IsNullOrWhiteSpace($nm)) {
      $names.Add($nm) | Out-Null
    }
    if ($current.PSObject.Properties.Name -contains "children" -and $current.children) {
      foreach ($child in $current.children) {
        Add-NamesRecursive $child
      }
    }
  }
  Add-NamesRecursive $node
  return $names
}

# Depth-first traversal without recursion
function Get-TreeItemsDepthFirst($root, [int]$MaxNodes = 200000) {
  $stack = New-Object System.Collections.Stack
  $stack.Push($root)
  $seen = 0
  while ($stack.Count -gt 0) {
    $cur = $stack.Pop()
    $seen++
    if ($seen -gt $MaxNodes) { throw "Traversal abgebrochen (zu viele Nodes, Loop?)" }
    $cur
    $cnt = Get-ChildCountSafe -item $cur -fallback 0
    for ($i=$cnt; $i -ge 1; $i--) {
      $ch = Get-ChildAtSafe -parent $cur -index $i
      if ($null -ne $ch) { $stack.Push($ch) }
    }
  }
}

function Find-ItemByName($root, [string]$Name, [int]$MaxNodes = 200000) {
  foreach ($it in Get-TreeItemsDepthFirst $root $MaxNodes) {
    if ((Get-ItemNameSafe $it "") -eq $Name) { return $it }
  }
  return $null
}

function Find-ItemByRegex($root, [string]$Pattern, [int]$MaxNodes = 200000) {
  $rx = [regex]::new($Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
  foreach ($it in Get-TreeItemsDepthFirst $root $MaxNodes) {
    if ($rx.IsMatch((Get-ItemNameSafe $it ""))) { return $it }
  }
  return $null
}

# -------------------- EtherCAT creation --------------------

function Set-PreviousPort($slaveItem, [string]$desiredPortLetter) {
  $xmlStr = $slaveItem.ProduceXml()
  [xml]$doc = $xmlStr

  $prevPorts = $doc.TreeItem.EtherCAT.Slave.PreviousPort
  if ($null -eq $prevPorts) {
    Write-Warning "Keine <PreviousPort>-Knoten gefunden fuer '$(Get-ItemNameSafe $slaveItem)'."
    return
  }

  $found = $false
  foreach ($pp in $prevPorts) {
    if ($pp.Attributes["Selected"]) { $pp.Attributes.RemoveNamedItem("Selected") | Out-Null }
    if ($pp.Port -eq $desiredPortLetter) {
      $pp.SetAttribute("Selected","1")
      $found = $true
    }
  }
  if (-not $found) {
    $portsAvail = ($prevPorts | ForEach-Object { $_.Port }) -join ","
    Write-Warning "Gewuenschter Port '$desiredPortLetter' ist fuer '$(Get-ItemNameSafe $slaveItem)' nicht verfuegbar. Verfuegbar: $portsAvail"
    return
  }

  $slaveItem.ConsumeXml($doc.OuterXml)
  Write-Info ("PreviousPort '{0}' gesetzt fuer '{1}'" -f $desiredPortLetter, (Get-ItemNameSafe $slaveItem))
}

function New-EbusChild($parent, [string]$name, [string]$vInfo) {
  # 9099 = generic E-Bus subtype for EK/EL (most cases)
  return $parent.CreateChild($name, 9099, $null, $vInfo)
}

function Build-EthercatNodeRecursive($parentItem, $node) {
  $item = $null
  $createdRaw = $parentItem.CreateChild([string]$node.name, 9099, $null, [string]$node.vInfo)
  if ($createdRaw -is [System.Array]) {
    if ($createdRaw.Count -eq 1) { $item = $createdRaw[0] }
  } else {
    $item = $createdRaw
  }

  if ($null -eq $item) {
    $item = Find-DirectChildByNameSafe -parent $parentItem -name ([string]$node.name)
  }
  if ($null -eq $item) {
    $item = Resolve-ExpectedChildByPath -parent $parentItem -name ([string]$node.name)
  }
  if ($null -eq $item) {
    throw ("CreateChild lieferte kein aufloesbares einzelnes Item fuer '{0}' ({1})." -f [string]$node.name, [string]$node.vInfo)
  }
  if ($item -is [System.Array]) {
    throw ("Aufgeloestes Kind fuer '{0}' ist weiterhin Object[] (Count={1})." -f [string]$node.name, $item.Count)
  }

  # Cache created items for deterministic linking in the same run.
  # Prefer path strings (stable across COM proxy refreshes).
  $itemPath = ""
  try { $itemPath = [string]$item.PathName } catch {}
  $cacheVal = if ([string]::IsNullOrWhiteSpace($itemPath)) { $item } else { $itemPath }
  $script:__CreatedTreeItemsByName[[string]$node.name] = $cacheVal
  $nk = Get-TerminalNameKey ([string]$node.name)
  if (-not [string]::IsNullOrWhiteSpace($nk)) {
    $script:__CreatedTreeItemsByName["__KEY__" + $nk] = $cacheVal
  }

  if ($node.PSObject.Properties.Name -contains "previousPort" -and $node.previousPort) {
    $item | Add-Member -NotePropertyName "__pendingPreviousPort" -NotePropertyValue ([string]$node.previousPort) -Force
  }

  if ($node.PSObject.Properties.Name -contains "children" -and $node.children) {
    foreach ($ch in $node.children) {
      Build-EthercatNodeRecursive -parentItem $item -node $ch
    }
  }
  return $item
}

function Apply-EthercatTree($sysman, $cfg) {
  $deviceName = $cfg.twincat.deviceName
  $ioRoot = $sysman.LookupTreeItem("TIID")

  if ($null -eq $cfg.ethercat -or $null -eq $cfg.ethercat.root) {
    throw "JSON unvollstaendig: cfg.ethercat.root fehlt"
  }

  $device = $null
  try {
    $device = $sysman.LookupTreeItem(("TIID^{0}" -f $deviceName))
    if ($device -is [System.Array]) {
      if ($device.Count -eq 1) { $device = $device[0] } else { throw "LookupTreeItem lieferte Object[] (Count=$($device.Count))" }
    }
    Write-Info "Verwende vorhandenes Geraet: $deviceName"
  } catch {
    throw ("EtherCAT-Master muss vorkonfiguriert sein und bereits existieren: 'TIID^{0}'. " +
           "Master manuell anlegen/konfigurieren (inkl. benoetigter Master-Links), dann erneut ausfuehren." -f $deviceName)
  }

  if ($device -is [System.Array]) {
    if ($device.Count -eq 1) { $device = $device[0] } else { throw "Create/Lookup fuer Geraet lieferte Object[] (Count=$($device.Count))" }
  }

  Write-Info "Erstelle EtherCAT-Topologie..."
  $script:__CreatedTreeItemsByName = @{}
  $device = Refresh-ItemByPath -item $device
  $remainingChildren = Get-ChildCountSafe -item $device -fallback 0
  if ($remainingChildren -gt 0) {
    throw (("Unter dem EtherCAT-Master '{0}' sind bereits {1} Kindknoten vorhanden. " +
            "Es wird nichts geloescht. Bitte den Master manuell leeren oder einen leeren Ziel-Master verwenden.") -f
            (Get-ItemNameSafe $device), $remainingChildren)
  }
  Build-EthercatNodeRecursiveSafe -parentItem $device -parentVInfo "TIID" -node $cfg.ethercat.root | Out-Null

  Write-Info "Wende ausstehende EK1122 PreviousPort-Auswahlen an..."
  Apply-PendingPreviousPorts -rootItem $device
}

# -------------------- GVL creation --------------------

function Render-GvlText([string]$gvlName, $ioPoints) {
  $lines = New-Object System.Collections.Generic.List[string]
  $lines.Add("{attribute 'qualified_only'}")
  $lines.Add("VAR_GLOBAL")
  $lines.Add("")

  $sorted = $ioPoints | Sort-Object terminalName, @{Expression={ [int]$_.channel }}
  $maxVarLen = 0
  $maxTypeLen = 0
  foreach ($p in $sorted) {
    $vnLen = ([string]$p.varName).Length
    if ($vnLen -gt $maxVarLen) { $maxVarLen = $vnLen }
    $t = if ($p.PSObject.Properties.Name -contains "type" -and $p.type) { [string]$p.type } else { "BOOL" }
    if ($t.Length -gt $maxTypeLen) { $maxTypeLen = $t.Length }
  }

  function Get-TabPad([int]$curLen, [int]$targetLen, [int]$tabSize = 4) {
    $delta = $targetLen - $curLen
    if ($delta -lt 1) { return "`t" }
    $tabs = [int][Math]::Ceiling($delta / $tabSize)
    if ($tabs -lt 1) { $tabs = 1 }
    return ("`t" * $tabs)
  }

  $currentTerm = $null
  foreach ($p in $sorted) {
    if ($p.terminalName -ne $currentTerm) {
      $currentTerm = $p.terminalName
      $lines.Add("    // $currentTerm")
    }
    $dir = ([string]$p.direction).ToUpperInvariant().Trim()
    $at  = if ($dir -eq "I") { "%I*" } else { "%Q*" }
    $typ = if ($p.PSObject.Properties.Name -contains "type" -and $p.type) { $p.type } else { "BOOL" }
    $grp = if ($p.PSObject.Properties.Name -contains "group") { [string]$p.group } else { "" }
    $desc = if ($p.PSObject.Properties.Name -contains "description") { [string]$p.description } else { "" }
    $comment = ("{0} {1}" -f $grp, $desc).Trim()
    $varName = [string]$p.varName
    $typeStr = [string]$typ
    $tabsAfterVar = Get-TabPad -curLen $varName.Length -targetLen ($maxVarLen + 2)
    $tabsAfterAt  = "`t"
    $tabsAfterTyp = Get-TabPad -curLen $typeStr.Length -targetLen ($maxTypeLen + 2)
    $lines.Add(("    {0}{1}AT {2}{3}: {4};{5}// {6}" -f $varName, $tabsAfterVar, $at, $tabsAfterAt, $typeStr, $tabsAfterTyp, $comment))
  }

  $lines.Add("")
  $lines.Add("END_VAR")
  return ($lines -join "`r`n")
}

function Set-GvlAlwaysBindTrue($sysman, $gvlItem, [string]$plcProjectName = "", [string]$gvlName = "") {
  if ($null -eq $gvlItem) {
    if (-not [string]::IsNullOrWhiteSpace($script:__LastGvlPath)) {
      for ($i = 1; $i -le 12 -and $null -eq $gvlItem; $i++) {
        $raw = Lookup-OrNull -sysman $sysman -path ([string]$script:__LastGvlPath)
        $gvlItem = Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath ([string]$script:__LastGvlPath) -expectedName $gvlName -expectedSubType -1
        if ($null -eq $gvlItem -and $raw -isnot [System.Array]) { $gvlItem = $raw }
        if ($null -eq $gvlItem) { Start-Sleep -Milliseconds 140 }
      }
    }
    if ($null -eq $gvlItem) {
      $gvlItem = Resolve-GvlItem -sysman $sysman -plcProjectName $plcProjectName -gvlName $gvlName
    }
    if ($null -eq $gvlItem) {
      Write-Warning ("'Immer binden' konnte fuer GVL '{0}' nicht gesetzt werden: Item-Handle ist null (auch nach Resolve-Retry)." -f $gvlName)
      return
    }
  }

  try {
    $xmlStr = Invoke-WithComRetry -Action { $gvlItem.ProduceXml() } -MaxAttempts 8 -SleepMs 180
    if ([string]::IsNullOrWhiteSpace([string]$xmlStr)) {
      Write-Warning ("'Immer binden' konnte fuer GVL '{0}' nicht gesetzt werden: ProduceXml lieferte leer." -f $gvlName)
      return
    }

    [xml]$doc = [string]$xmlStr
    $changed = $false

    foreach ($n in $doc.SelectNodes("//*")) {
      if ($null -eq $n) { continue }

      $nodeName = ""
      try { $nodeName = [string]$n.Name } catch {}
      $inner = ""
      try { $inner = [string]$n.InnerText } catch {}

      if ($nodeName -match "^(AlwaysLink|AlwaysBind)$") {
        if ($inner -ne "1") { $n.InnerText = "1"; $changed = $true }
      }

      if ($n.Attributes) {
        $nameAttr = $n.Attributes["Name"]
        if ($null -ne $nameAttr -and [string]$nameAttr.Value -match "^(AlwaysLink|AlwaysBind)$") {
          $valueAttr = $n.Attributes["Value"]
          if ($null -ne $valueAttr) {
            if ([string]$valueAttr.Value -ne "1") { $valueAttr.Value = "1"; $changed = $true }
          } else {
            $newAttr = $doc.CreateAttribute("Value")
            $newAttr.Value = "1"
            [void]$n.Attributes.Append($newAttr)
            $changed = $true
          }
        }
      }
    }

    foreach ($prop in $doc.SelectNodes("//*[Name and Value]")) {
      $nameNode = $prop.SelectSingleNode("Name")
      $valueNode = $prop.SelectSingleNode("Value")
      if ($null -eq $nameNode -or $null -eq $valueNode) { continue }
      $pname = [string]$nameNode.InnerText
      if ($pname -match "^(AlwaysLink|AlwaysBind)$") {
        if ([string]$valueNode.InnerText -ne "1") { $valueNode.InnerText = "1"; $changed = $true }
      }
    }

    if ($changed) {
      Invoke-WithComRetry -Action { $gvlItem.ConsumeXml($doc.OuterXml) } -MaxAttempts 8 -SleepMs 180 | Out-Null
      Write-Info ("GVL property gesetzt: Immer binden=True ({0})" -f $gvlName)
    } else {
      Write-Warning ("AlwaysLink/AlwaysBind XML-Feld in GVL '{0}' nicht gefunden. Eigenschaft unveraendert." -f $gvlName)
    }
  } catch {
    Write-Warning ("Setzen von 'Immer binden=True' fuer GVL '{0}' fehlgeschlagen: {1}" -f $gvlName, $_.Exception.Message)
  }
}

function Ensure-Gvl($sysman, [string]$plcProjectName, [string]$gvlName, [string]$declText) {
  $gvlFolder = $null
  $legacyGvlPath = "TIPC^{0}^{0} Project^GVLs" -f $plcProjectName
  $legacyRaw = Lookup-OrNull $sysman $legacyGvlPath
  $gvlFolder = Select-SingleTreeItemFromLookup -lookupResult $legacyRaw -expectedPath $legacyGvlPath -expectedName "GVLs" -expectedSubType -1

  if ($null -eq $gvlFolder) {
    $tipc = Lookup-OrNull $sysman "TIPC"
    $tipcRoots = @()
    if ($tipc -is [System.Array]) { $tipcRoots = @($tipc) }
    elseif ($null -ne $tipc) { $tipcRoots = @($tipc) }

    $gvlCandidates = New-Object System.Collections.Generic.List[object]
    foreach ($r in $tipcRoots) {
      foreach ($it in Get-TreeItemsDepthFirst $r 300000) {
        $n = ""
        $p = ""
        try { $n = [string]$it.Name } catch {}
        if ($n -ne "GVLs") { continue }
        try { $p = [string]$it.PathName } catch {}
        if ($p -match "\^GVLs$") {
          $gvlCandidates.Add($it) | Out-Null
        }
      }
    }

    if ($gvlCandidates.Count -gt 0) {
      $plcRx = [regex]::Escape($plcProjectName)
      foreach ($c in $gvlCandidates) {
        $cp = ""
        try { $cp = [string]$c.PathName } catch {}
        if ($cp -match ("(\^|\\b){0}(\^|\\b)" -f $plcRx)) {
          $gvlFolder = $c
          break
        }
      }
      if ($null -eq $gvlFolder) { $gvlFolder = $gvlCandidates[0] }
    }
  }

  if ($null -eq $gvlFolder) {
    throw ("GVLs-Knoten fuer PLC-Projekt '{0}' nicht gefunden. Geprueft: Pfad '{1}' und TIPC-Tree-Suche." -f $plcProjectName, $legacyGvlPath)
  }

  $folderPath = ""
  try { $folderPath = [string]$gvlFolder.PathName } catch {}
  Write-Info ("Erstelle GVL: {0}" -f $gvlName)
  # 615 = GVL
  $createdRaw = Invoke-WithComRetry -Action { $gvlFolder.CreateChild($gvlName, 615, $null, $declText) } -MaxAttempts 8 -SleepMs 180
  $created = $null
  if ($createdRaw -is [System.Array]) {
    if ($createdRaw.Count -eq 1) { $created = $createdRaw[0] }
  } else {
    $created = $createdRaw
  }
  if ($null -eq $created -and -not [string]::IsNullOrWhiteSpace($folderPath)) {
    $createdPath = "{0}^{1}" -f $folderPath, $gvlName
    $rawCreated = Lookup-OrNull -sysman $sysman -path $createdPath
    $created = Select-SingleTreeItemFromLookup -lookupResult $rawCreated -expectedPath $createdPath -expectedName $gvlName -expectedSubType -1
    if ($null -eq $created -and $rawCreated -isnot [System.Array]) { $created = $rawCreated }
  }
  if ($null -eq $created) { Write-Warning ("GVL wurde erstellt, aber TreeItem-Handle konnte nicht sofort aufgeloest werden: {0}" -f $gvlName) }
  $script:__LastGvlItem = $created
  $script:__LastGvlPath = ""
  try {
    $folderPath = [string]$gvlFolder.PathName
    if (-not [string]::IsNullOrWhiteSpace($folderPath)) {
      $script:__LastGvlPath = "{0}^{1}" -f $folderPath, $gvlName
    }
  } catch {}
  if ([string]::IsNullOrWhiteSpace($script:__LastGvlPath) -and $null -ne $script:__LastGvlItem) {
    try { $script:__LastGvlPath = [string]$script:__LastGvlItem.PathName } catch {}
  }
  Set-GvlAlwaysBindTrue -sysman $sysman -gvlItem $script:__LastGvlItem -plcProjectName $plcProjectName -gvlName $gvlName
  return $script:__LastGvlItem
}

# -------------------- Linking --------------------

function Invoke-WithComRetry([scriptblock]$Action, [int]$MaxAttempts = 8, [int]$SleepMs = 180) {
  $attempt = 0
  while ($true) {
    $attempt++
    try {
      return & $Action
    } catch {
      $msg = ""
      try { $msg = [string]$_.Exception.Message } catch {}
      $isRejected = ($msg -match "0x80010001") -or ($msg -match "RPC_E_CALL_REJECTED") -or ($msg -match "Aufruf wurde durch Aufgerufenen abgelehnt")
      $isRpcUnavailable = ($msg -match "0x800706BA") -or ($msg -match "RPC server is unavailable") -or ($msg -match "RPC-Server ist nicht verfügbar")
      if ($isRpcUnavailable) {
        try { Refresh-TwinCatSession | Out-Null } catch {}
      }
      if ((-not $isRejected) -and (-not $isRpcUnavailable)) { throw }
      if ($attempt -ge $MaxAttempts) { throw }
      $waitMs = if ($isRpcUnavailable) { [Math]::Max($SleepMs, 450) } else { $SleepMs }
      Start-Sleep -Milliseconds $waitMs
    }
  }
}

function Get-TerminalNameKey([string]$name) {
  if ([string]::IsNullOrWhiteSpace($name)) { return "" }
  $s = $name.Trim().ToUpperInvariant()
  $idx = $s.IndexOf(" (")
  if ($idx -gt 0) { $s = $s.Substring(0, $idx) }
  $s = $s -replace '\s+', ''
  return $s
}

function Find-TerminalItem($sysman, [string]$deviceName, [string]$terminalName) {
  function Resolve-CachedTerminal($cached, $sysmanInner) {
    if ($null -eq $cached) { return $null }
    if ($cached -is [string]) {
      $raw = Lookup-OrNull $sysmanInner ([string]$cached)
      $hit = Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath ([string]$cached) -expectedName "" -expectedSubType -1
      if ($null -ne $hit) { return $hit }
      if ($raw -isnot [System.Array]) { return $raw }
      return $null
    }
    return (Refresh-ItemByPath -item $cached)
  }

  if ($script:__CreatedTreeItemsByName.ContainsKey([string]$terminalName)) {
    $cachedHit = Resolve-CachedTerminal -cached $script:__CreatedTreeItemsByName[[string]$terminalName] -sysmanInner $sysman
    if ($null -ne $cachedHit) { return $cachedHit }
  }
  $terminalKey = Get-TerminalNameKey $terminalName
  if (-not [string]::IsNullOrWhiteSpace($terminalKey)) {
    $dictKey = "__KEY__" + $terminalKey
    if ($script:__CreatedTreeItemsByName.ContainsKey($dictKey)) {
      $cachedHit = Resolve-CachedTerminal -cached $script:__CreatedTreeItemsByName[$dictKey] -sysmanInner $sysman
      if ($null -ne $cachedHit) { return $cachedHit }
    }
  }

  # Most reliable in your environment: resolve master via IO root + subtype 111.
  $device = $null
  $ioRoot = Invoke-WithComRetry -Action { $sysman.LookupTreeItem("TIID") }
  $device = Find-IoRootChildByNameAndSubtype -ioRoot $ioRoot -name $deviceName -subType 111

  if ($null -eq $device) {
    $deviceRaw = Invoke-WithComRetry -Action { $sysman.LookupTreeItem(("TIID^{0}" -f $deviceName)) }
    $device = Select-SingleTreeItemFromLookup -lookupResult $deviceRaw -expectedPath ("TIID^{0}" -f $deviceName) -expectedName $deviceName -expectedSubType 111
    if ($null -eq $device) {
      if ($deviceRaw -is [System.Array] -and $deviceRaw.Count -eq 1) { $device = $deviceRaw[0] }
      elseif ($deviceRaw -isnot [System.Array]) { $device = $deviceRaw }
    }
  }
  if ($null -eq $device) { throw "Geraet unter TIID nicht gefunden oder nicht eindeutig: $deviceName" }

  $term = Find-ItemByName -root $device -Name $terminalName -MaxNodes 150000
  if ($null -ne $term) { return $term }

  $targetKey = Get-TerminalNameKey $terminalName
  if (-not [string]::IsNullOrWhiteSpace($targetKey)) {
    foreach ($it in Get-TreeItemsDepthFirst $device 200000) {
      $nm = ""
      try { $nm = [string]$it.Name } catch {}
      if ([string]::IsNullOrWhiteSpace($nm)) { continue }
      $k = Get-TerminalNameKey $nm
      if ($k -eq $targetKey) { return $it }
      if ($k.StartsWith($targetKey) -or $targetKey.StartsWith($k)) { return $it }
    }
  }

  if ($null -eq $term) { throw "Terminal unter Geraet '$deviceName' nicht gefunden: $terminalName" }
  return $term
}

function Find-IoLeaf($terminalItem, [int]$channel, [string]$direction, [string]$ioLeafOverride = "") {
  # Refresh terminal proxy by path to avoid stale COM objects during linking.
  if ($null -ne $script:__TcSysMan) {
    $tPath = ""
    try { $tPath = [string]$terminalItem.PathName } catch {}
    if (-not [string]::IsNullOrWhiteSpace($tPath)) {
      $rawTerm = Lookup-OrNull -sysman $script:__TcSysMan -path $tPath
      $freshTerm = Select-SingleTreeItemFromLookup -lookupResult $rawTerm -expectedPath $tPath -expectedName "" -expectedSubType -1
      if ($null -ne $freshTerm) { $terminalItem = $freshTerm }
    }
  }

  $dir = $direction.ToUpperInvariant().Trim()
  $leafName = if ($ioLeafOverride) { $ioLeafOverride } else { if ($dir -eq "I") { "Input" } else { "Output" } }
  $chRx = ("(?i)\b(Channel|Kanal)\s+{0}\b" -f $channel)
  $ioInRx = ("(?i)^IO\s+Inputs?\s+Channel\s+{0}\b" -f $channel)
  $ioOutRx = ("(?i)^IO\s+Outputs?\s+Channel\s+{0}\b" -f $channel)

  $best = $null
  $bestScore = -1
  foreach ($it in Get-TreeItemsDepthFirst $terminalItem 70000) {
    $nm = Get-ItemNameSafe $it ""
    if ([string]::IsNullOrWhiteSpace($nm)) { continue }
    $pp = ""
    try { $pp = [string]$it.PathName } catch {}

    $isChannelMatch = ($nm -match $chRx) -or ($pp -match $chRx) -or ($nm -match $ioInRx) -or ($nm -match $ioOutRx)
    if (-not $isChannelMatch) { continue }

    $score = -1
    if ($ioLeafOverride -and $nm -ieq $ioLeafOverride) { $score = 200 }
    elseif ($dir -eq "I") {
      if ($nm -ieq "Input") { $score = 120 }
      elseif ($nm -ieq "Inputs") { $score = 115 }
      elseif ($nm -match $ioInRx) { $score = 110 }
      elseif ($nm -match $chRx) { $score = 95 }
      elseif ($nm -imatch "Input") { $score = 90 }
      elseif ($nm -imatch "Value") { $score = 70 }
    } else {
      if ($nm -ieq "Output") { $score = 120 }
      elseif ($nm -ieq "Outputs") { $score = 115 }
      elseif ($nm -match $ioOutRx) { $score = 110 }
      elseif ($nm -match $chRx) { $score = 95 }
      elseif ($nm -imatch "Output") { $score = 90 }
      elseif ($nm -imatch "Value") { $score = 70 }
    }

    if ($score -gt $bestScore) {
      $best = $it
      $bestScore = $score
    }
  }

  if ($null -ne $best) { return $best }
  throw "IO-Leaf nicht gefunden fuer $(Get-ItemNameSafe $terminalItem) Kanal $channel ($direction), Leaf '$leafName'"
}

function Resolve-GvlItem($sysman, [string]$plcProjectName, [string]$gvlName) {
  if ([string]::IsNullOrWhiteSpace($gvlName)) { return $null }

  $pathCandidates = New-Object System.Collections.Generic.List[string]
  if (-not [string]::IsNullOrWhiteSpace($script:__LastGvlPath)) {
    $pathCandidates.Add([string]$script:__LastGvlPath) | Out-Null
  }
  if (-not [string]::IsNullOrWhiteSpace($plcProjectName)) {
    $pathCandidates.Add(("TIPC^{0}^{0} Project^GVLs^{1}" -f $plcProjectName, $gvlName)) | Out-Null
  }

  foreach ($p in ($pathCandidates | Select-Object -Unique)) {
    $raw = Lookup-OrNull -sysman $sysman -path $p
    $hit = Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath $p -expectedName $gvlName -expectedSubType -1
    if ($null -eq $hit -and $raw -isnot [System.Array]) { $hit = $raw }
    if ($null -ne $hit) { return $hit }
  }

  if ($null -ne $script:__LastGvlItem) {
    $nm = Get-ItemNameSafe $script:__LastGvlItem ""
    if ($nm -eq $gvlName) { return $script:__LastGvlItem }
  }

  $tipc = Lookup-OrNull $sysman "TIPC"
  $tipcRoots = @()
  if ($tipc -is [System.Array]) { $tipcRoots = @($tipc) }
  elseif ($null -ne $tipc) { $tipcRoots = @($tipc) }

  $plcRx = [regex]::Escape([string]$plcProjectName)
  foreach ($r in $tipcRoots) {
    foreach ($it in Get-TreeItemsDepthFirst $r 300000) {
      $nm = Get-ItemNameSafe $it ""
      if ($nm -ne $gvlName) { continue }
      $pp = ""
      try { $pp = [string]$it.PathName } catch {}
      if ([string]::IsNullOrWhiteSpace($pp)) { continue }
      if ($pp -notmatch "\^GVLs\^") { continue }
      if (-not [string]::IsNullOrWhiteSpace($plcProjectName) -and $pp -notmatch ("(\^|\\b){0}(\^|\\b)" -f $plcRx)) { continue }
      return $it
    }
  }
  return $null
}

function Find-GvlDeclarationVarItem($sysman, [string]$plcProjectName, [string]$gvlName, [string]$varName) {
  if ([string]::IsNullOrWhiteSpace($gvlName) -or [string]::IsNullOrWhiteSpace($varName)) { return $null }
  $cacheKey = ("{0}|{1}|{2}" -f $plcProjectName, $gvlName, $varName).ToLowerInvariant()
  if ($script:__GvlDeclVarCache.ContainsKey($cacheKey)) {
    $cached = $script:__GvlDeclVarCache[$cacheKey]
    if ($cached -is [string]) {
      $raw = Lookup-OrNull -sysman $sysman -path ([string]$cached)
      $hit = Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath ([string]$cached) -expectedName $varName -expectedSubType -1
      if ($null -eq $hit -and $raw -isnot [System.Array]) { $hit = $raw }
      if ($null -ne $hit) { return $hit }
    } elseif ($null -ne $cached) {
      return $cached
    }
  }

  $gvlItem = Resolve-GvlItem -sysman $sysman -plcProjectName $plcProjectName -gvlName $gvlName
  if ($null -eq $gvlItem) { return $null }

  $declVar = Find-ItemByName -root $gvlItem -Name $varName -MaxNodes 120000
  if ($null -eq $declVar) { return $null }

  $path = ""
  try { $path = [string]$declVar.PathName } catch {}
  if (-not [string]::IsNullOrWhiteSpace($path)) {
    $script:__GvlDeclVarCache[$cacheKey] = $path
  } else {
    $script:__GvlDeclVarCache[$cacheKey] = $declVar
  }
  return $declVar
}

function Get-PlcVarCacheKey([string]$plcProjectName, [string]$gvlName, [string]$varName, [string]$direction) {
  return ("{0}|{1}|{2}|{3}" -f [string]$plcProjectName, [string]$gvlName, [string]$varName, ([string]$direction).ToUpperInvariant().Trim()).ToLowerInvariant()
}

function Resolve-PlcTaskBasePaths($sysman, [string]$plcProjectName, [string]$direction) {
  $dir = ([string]$direction).ToUpperInvariant().Trim()
  if ($script:__PlcTaskBasePathByDir.ContainsKey($dir)) {
    return @($script:__PlcTaskBasePathByDir[$dir])
  }

  $taskFolders = if ($dir -eq "Q") { @("PlcTask Outputs", "PlcTask Ausgaenge", "PlcTask Ausgänge") } else { @("PlcTask Inputs", "PlcTask Eingaenge", "PlcTask Eingänge") }
  $plcNames = @([string]$plcProjectName, "PLC") | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique
  $candidates = New-Object System.Collections.Generic.List[string]

  foreach ($pn in $plcNames) {
    $instNames = @("PLC Instance", ("{0} Instance" -f $pn)) | Select-Object -Unique
    foreach ($inst in $instNames) {
      foreach ($tf in $taskFolders) {
        $candidates.Add(("TIPC^{0}^{1}^{2}" -f $pn, $inst, $tf)) | Out-Null
      }
    }
  }

  $hits = New-Object System.Collections.Generic.List[string]
  foreach ($base in ($candidates | Select-Object -Unique)) {
    $raw = Lookup-OrNull -sysman $sysman -path $base
    $hit = Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath $base -expectedName "" -expectedSubType -1
    if ($null -eq $hit -and $raw -isnot [System.Array]) { $hit = $raw }
    if ($null -ne $hit) { $hits.Add([string]$base) | Out-Null }
  }

  if ($hits.Count -eq 0) {
    foreach ($base in ($candidates | Select-Object -Unique)) { $hits.Add([string]$base) | Out-Null }
  }
  $script:__PlcTaskBasePathByDir[$dir] = @($hits | Select-Object -Unique)
  return @($script:__PlcTaskBasePathByDir[$dir])
}

function Find-PlcVarItem($sysman, [string]$plcProjectName, [string]$gvlName, [string]$varName, [string]$direction) {
  $full = "{0}.{1}" -f $gvlName, $varName
  $dir = ([string]$direction).ToUpperInvariant().Trim()
  $cacheKey = Get-PlcVarCacheKey -plcProjectName $plcProjectName -gvlName $gvlName -varName $varName -direction $dir

  function Normalize-SymbolText([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return "" }
    $n = $s.ToLowerInvariant()
    $n = $n -replace '\s+', ''
    $n = $n -replace '\^', '.'
    return $n
  }

  function Resolve-ItemByPath($sysmanInner, [string]$candPath, [string]$nameHint) {
    $raw = Lookup-OrNull -sysman $sysmanInner -path $candPath
    $hit = Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath $candPath -expectedName $nameHint -expectedSubType -1
    if ($null -eq $hit -and $raw -isnot [System.Array]) { $hit = $raw }
    return $hit
  }

  if ($script:__PlcVarPathCache.ContainsKey($cacheKey)) {
    $cachedPath = [string]$script:__PlcVarPathCache[$cacheKey]
    if (-not [string]::IsNullOrWhiteSpace($cachedPath)) {
      $cachedHit = Resolve-ItemByPath -sysmanInner $sysman -candPath $cachedPath -nameHint ""
      if ($null -ne $cachedHit) { return $cachedHit }
    }
  }

  $fullNorm = Normalize-SymbolText $full
  $varNorm = Normalize-SymbolText $varName

  $basePaths = Resolve-PlcTaskBasePaths -sysman $sysman -plcProjectName $plcProjectName -direction $dir
  foreach ($base in $basePaths) {
    foreach ($sym in @($full, $varName)) {
      $cand = "{0}^{1}" -f [string]$base, [string]$sym
      $byPath = Resolve-ItemByPath -sysmanInner $sysman -candPath $cand -nameHint ""
      if ($null -ne $byPath) {
        try {
          $bp = [string]$byPath.PathName
          if (-not [string]::IsNullOrWhiteSpace($bp)) { $script:__PlcVarPathCache[$cacheKey] = $bp }
        } catch {}
        return $byPath
      }
    }
  }

  $instRoots = Get-PlcInstanceRoots -sysman $sysman -plcProjectName $plcProjectName
  $best = $null
  $bestScore = -1

  foreach ($inst in $instRoots) {
    $hit = Find-ItemByName -root $inst -Name $full -MaxNodes 250000
    if ($null -ne $hit) {
      try {
        $hp = [string]$hit.PathName
        if (-not [string]::IsNullOrWhiteSpace($hp)) { $script:__PlcVarPathCache[$cacheKey] = $hp }
      } catch {}
      return $hit
    }

    $hit2 = Find-ItemByName -root $inst -Name $varName -MaxNodes 250000
    if ($null -ne $hit2) {
      try {
        $hp2 = [string]$hit2.PathName
        if (-not [string]::IsNullOrWhiteSpace($hp2)) { $script:__PlcVarPathCache[$cacheKey] = $hp2 }
      } catch {}
      return $hit2
    }

    foreach ($it in Get-TreeItemsDepthFirst $inst 300000) {
      $nm = Get-ItemNameSafe $it ""
      if ([string]::IsNullOrWhiteSpace($nm)) { continue }
      $pp = ""
      try { $pp = [string]$it.PathName } catch {}

      $nmN = Normalize-SymbolText $nm
      $ppN = Normalize-SymbolText $pp
      if ([string]::IsNullOrWhiteSpace($nmN) -and [string]::IsNullOrWhiteSpace($ppN)) { continue }

      $score = -1
      if ($nmN -eq $fullNorm) { $score = 400 }
      elseif ($ppN -eq $fullNorm -or $ppN.EndsWith("." + $fullNorm)) { $score = 390 }
      elseif ($nmN.EndsWith("." + $fullNorm) -or $nmN.Contains("." + $fullNorm + ".")) { $score = 370 }
      elseif ($ppN.Contains("." + $fullNorm + ".")) { $score = 360 }
      elseif ($nmN -eq $varNorm) { $score = 300 }
      elseif ($ppN.EndsWith("." + $varNorm)) { $score = 290 }
      elseif ($nmN.EndsWith("." + $varNorm) -or $nmN.Contains("." + $varNorm + ".")) { $score = 270 }
      elseif ($ppN.Contains("." + $varNorm + ".")) { $score = 260 }

      if ($score -ge 0) {
        if ($dir -eq "I") {
          if ($ppN.Contains(".plctaskinputs.") -or $nmN.Contains(".plctaskinputs.")) { $score += 12 }
        } elseif ($dir -eq "Q") {
          if ($ppN.Contains(".plctaskoutputs.") -or $nmN.Contains(".plctaskoutputs.")) { $score += 12 }
        }
      }

      if ($score -gt $bestScore) {
        $bestScore = $score
        $best = $it
      }
    }
  }

  if ($null -ne $best) {
    try {
      $bp3 = [string]$best.PathName
      if (-not [string]::IsNullOrWhiteSpace($bp3)) { $script:__PlcVarPathCache[$cacheKey] = $bp3 }
    } catch {}
    return $best
  }

  # Fallback: use declaration symbol directly from the GVL tree when process image is delayed/unavailable.
  $declFallback = Find-GvlDeclarationVarItem -sysman $sysman -plcProjectName $plcProjectName -gvlName $gvlName -varName $varName
  if ($null -ne $declFallback) { return $declFallback }

  return $null
}

function Try-LinkByDirectPlcPath($sysman, [string]$plcProjectName, [string]$gvlName, [string]$varName, [string]$direction, [string]$ioPath) {
  if ([string]::IsNullOrWhiteSpace($gvlName) -or [string]::IsNullOrWhiteSpace($varName) -or [string]::IsNullOrWhiteSpace($ioPath)) { return $null }
  $dir = ([string]$direction).ToUpperInvariant().Trim()
  $cacheKey = Get-PlcVarCacheKey -plcProjectName $plcProjectName -gvlName $gvlName -varName $varName -direction $dir

  if ($script:__PlcVarPathCache.ContainsKey($cacheKey)) {
    $cachedPath = [string]$script:__PlcVarPathCache[$cacheKey]
    if (-not [string]::IsNullOrWhiteSpace($cachedPath)) {
      try {
        Invoke-WithComRetry -Action { $sysman.LinkVariables([string]$cachedPath, [string]$ioPath, 0, 0, 0) } | Out-Null
        return [string]$cachedPath
      } catch {}
    }
  }

  $sym = "{0}.{1}" -f $gvlName, $varName
  $symCandidates = @($sym, $varName) | Select-Object -Unique

  $paths = New-Object System.Collections.Generic.List[string]
  $basePaths = Resolve-PlcTaskBasePaths -sysman $sysman -plcProjectName $plcProjectName -direction $dir
  foreach ($base in $basePaths) {
    foreach ($s in $symCandidates) {
      $paths.Add(("{0}^{1}" -f [string]$base, [string]$s)) | Out-Null
    }
  }

  foreach ($plcPath in ($paths | Select-Object -Unique)) {
    try {
      Invoke-WithComRetry -Action { $sysman.LinkVariables([string]$plcPath, [string]$ioPath, 0, 0, 0) } | Out-Null
      $script:__PlcVarPathCache[$cacheKey] = [string]$plcPath
      return [string]$plcPath
    } catch {}
  }
  return $null
}

function Build-SchemaTerminalPathIndex($cfg) {
  $index = @{}
  if ($null -eq $cfg -or $null -eq $cfg.twincat -or $null -eq $cfg.ethercat -or $null -eq $cfg.ethercat.root) { return $index }
  $deviceName = [string]$cfg.twincat.deviceName
  if ([string]::IsNullOrWhiteSpace($deviceName)) { return $index }

  function Add-Path([hashtable]$idx, [string]$name, [string]$path) {
    if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($path)) { return }
    if (-not $idx.ContainsKey($name)) { $idx[$name] = New-Object System.Collections.Generic.List[string] }
    if (-not $idx[$name].Contains($path)) { $idx[$name].Add($path) | Out-Null }

    $k = Get-TerminalNameKey $name
    if (-not [string]::IsNullOrWhiteSpace($k)) {
      $kk = "__KEY__" + $k
      if (-not $idx.ContainsKey($kk)) { $idx[$kk] = New-Object System.Collections.Generic.List[string] }
      if (-not $idx[$kk].Contains($path)) { $idx[$kk].Add($path) | Out-Null }
    }
  }

  function Walk($node, [string]$parentPath, [hashtable]$idx) {
    if ($null -eq $node) { return }
    $name = [string]$node.name
    if ([string]::IsNullOrWhiteSpace($name)) { return }
    $curPath = "{0}^{1}" -f $parentPath, $name
    Add-Path -idx $idx -name $name -path $curPath
    if ($node.PSObject.Properties.Name -contains "children" -and $node.children) {
      foreach ($ch in $node.children) { Walk -node $ch -parentPath $curPath -idx $idx }
    }
  }

  $rootPath = "TIID^{0}" -f $deviceName
  Walk -node $cfg.ethercat.root -parentPath $rootPath -idx $index
  return $index
}

function Resolve-TerminalBySchemaPath($sysman, [hashtable]$terminalPathsIndex, [string]$terminalName) {
  if ($null -eq $terminalPathsIndex) { return $null }
  $candidates = New-Object System.Collections.Generic.List[string]
  if ($terminalPathsIndex.ContainsKey($terminalName)) {
    foreach ($p in $terminalPathsIndex[$terminalName]) { $candidates.Add([string]$p) | Out-Null }
  }
  $k = Get-TerminalNameKey $terminalName
  if (-not [string]::IsNullOrWhiteSpace($k)) {
    $kk = "__KEY__" + $k
    if ($terminalPathsIndex.ContainsKey($kk)) {
      foreach ($p in $terminalPathsIndex[$kk]) { if (-not $candidates.Contains([string]$p)) { $candidates.Add([string]$p) | Out-Null } }
    }
  }
  foreach ($tp in $candidates) {
    $raw = Lookup-OrNull $sysman $tp
    $hit = Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath $tp -expectedName "" -expectedSubType -1
    if ($null -eq $hit -and $raw -isnot [System.Array]) { $hit = $raw }
    if ($null -ne $hit) { return $hit }
  }
  return $null
}

function Resolve-IoLeafBySchemaPath($sysman, [hashtable]$terminalPathsIndex, [string]$terminalName, [int]$channel, [string]$direction) {
  if ($null -eq $terminalPathsIndex -or -not $terminalPathsIndex.ContainsKey($terminalName)) { return $null }
  $dir = ([string]$direction).ToUpperInvariant().Trim()
  $leaf = if ($dir -eq "I") { "Input" } else { "Output" }

  foreach ($termPath in $terminalPathsIndex[$terminalName]) {
    $candidates = @(
      ("{0}^Channel {1}^{2}" -f $termPath, $channel, $leaf),
      ("{0}^Kanal {1}^{2}" -f $termPath, $channel, $leaf),
      ("{0}^{1} Inputs Channel {2}" -f $termPath, "IO", $channel),
      ("{0}^{1} Outputs Channel {2}" -f $termPath, "IO", $channel)
    )
    foreach ($p in ($candidates | Select-Object -Unique)) {
      $raw = Lookup-OrNull $sysman $p
      $hit = Select-SingleTreeItemFromLookup -lookupResult $raw -expectedPath $p -expectedName "" -expectedSubType -1
      if ($null -eq $hit -and $raw -isnot [System.Array]) { $hit = $raw }
      if ($null -ne $hit) { return $hit }
    }
  }
  return $null
}

function Get-IoLeafPathCandidatesFromSchema([hashtable]$terminalPathsIndex, [string]$terminalName, [int]$channel, [string]$direction) {
  $result = New-Object System.Collections.Generic.List[string]
  if ($null -eq $terminalPathsIndex) { return @($result) }

  $termCandidates = New-Object System.Collections.Generic.List[string]
  if ($terminalPathsIndex.ContainsKey($terminalName)) {
    foreach ($p in $terminalPathsIndex[$terminalName]) { $termCandidates.Add([string]$p) | Out-Null }
  }
  $k = Get-TerminalNameKey $terminalName
  if (-not [string]::IsNullOrWhiteSpace($k)) {
    $kk = "__KEY__" + $k
    if ($terminalPathsIndex.ContainsKey($kk)) {
      foreach ($p in $terminalPathsIndex[$kk]) {
        $ps = [string]$p
        if (-not $termCandidates.Contains($ps)) { $termCandidates.Add($ps) | Out-Null }
      }
    }
  }

  $dir = ([string]$direction).ToUpperInvariant().Trim()
  $leaf = if ($dir -eq "I") { "Input" } else { "Output" }
  foreach ($termPath in $termCandidates) {
    $cands = @(
      ("{0}^Channel {1}^{2}" -f $termPath, $channel, $leaf),
      ("{0}^Kanal {1}^{2}" -f $termPath, $channel, $leaf),
      ("{0}^{1} Inputs Channel {2}" -f $termPath, "IO", $channel),
      ("{0}^{1} Outputs Channel {2}" -f $termPath, "IO", $channel)
    )
    foreach ($c in ($cands | Select-Object -Unique)) {
      if (-not $result.Contains([string]$c)) { $result.Add([string]$c) | Out-Null }
    }
  }
  return @($result)
}

function Get-PlcInstanceRoots($sysman, [string]$plcProjectName) {
  $roots = New-Object System.Collections.Generic.List[object]

  $instPath = "TIPC^{0}^{0} Instance" -f $plcProjectName
  $byPath = Lookup-OrNull $sysman $instPath
  if ($byPath -is [System.Array]) {
    foreach ($x in $byPath) { if ($null -ne $x) { $roots.Add($x) | Out-Null } }
  } elseif ($null -ne $byPath) {
    $roots.Add($byPath) | Out-Null
  }

  $tipc = Lookup-OrNull $sysman "TIPC"
  $tipcRoots = @()
  if ($tipc -is [System.Array]) { $tipcRoots = @($tipc) }
  elseif ($null -ne $tipc) { $tipcRoots = @($tipc) }

  foreach ($r in $tipcRoots) {
    $named = Find-ItemByName -root $r -Name ("{0} Instance" -f $plcProjectName) -MaxNodes 250000
    if ($null -ne $named) { $roots.Add($named) | Out-Null }
    foreach ($it in Get-TreeItemsDepthFirst $r 300000) {
      $nm = Get-ItemNameSafe $it ""
      if ($nm -match " Instance$") { $roots.Add($it) | Out-Null }
    }
  }

  $byPathMap = @{}
  foreach ($r in $roots) {
    $p = ""
    try { $p = [string]$r.PathName } catch {}
    if ([string]::IsNullOrWhiteSpace($p)) { continue }
    if (-not $byPathMap.ContainsKey($p)) { $byPathMap[$p] = $r }
  }
  return @($byPathMap.Values)
}

function Apply-PreCompileMasterLink($sysman, $cfg) {
  if ($null -eq $cfg.twincat) { return }
  $deviceName = [string]$cfg.twincat.deviceName
  $plcProjectName = [string]$cfg.twincat.plcProjectName
  if ([string]::IsNullOrWhiteSpace($deviceName) -or [string]::IsNullOrWhiteSpace($plcProjectName)) { return }

  $ioPrePath = "TIID^{0}^Inputs^DevState" -f $deviceName
  try {
    if ($cfg.twincat.PSObject.Properties.Name -contains "preCompileMasterIoPath") {
      $cfgPath = [string]$cfg.twincat.preCompileMasterIoPath
      if (-not [string]::IsNullOrWhiteSpace($cfgPath)) { $ioPrePath = $cfgPath }
    }
  } catch {}

  $ioCandidates = @($ioPrePath, ("TIID^{0}^Eingänge^DevState" -f $deviceName), ("TIID^{0}^Inputs^FrmDevState" -f $deviceName), ("TIID^{0}^Eingänge^FrmDevState" -f $deviceName))
  $ioCandidates = $ioCandidates | Select-Object -Unique
  $ioItem = $null
  foreach ($cand in $ioCandidates) {
    $ioRaw = Lookup-OrNull $sysman $cand
    $ioItem = Select-SingleTreeItemFromLookup -lookupResult $ioRaw -expectedPath $cand -expectedName "" -expectedSubType -1
    if ($null -eq $ioItem -and $ioRaw -isnot [System.Array]) { $ioItem = $ioRaw }
    if ($null -ne $ioItem) { break }
  }
  if ($null -eq $ioItem) {
    # fallback: match leaf name from the configured path under the device subtree
    $leafName = ""
    if ($ioPrePath -match "\^([^^]+)$") { $leafName = $matches[1] }
    $devPath = "TIID^{0}" -f $deviceName
    $devRaw = Lookup-OrNull $sysman $devPath
    $dev = Select-SingleTreeItemFromLookup -lookupResult $devRaw -expectedPath $devPath -expectedName $deviceName -expectedSubType 111
    if ($null -eq $dev -and $devRaw -isnot [System.Array]) { $dev = $devRaw }
    if ($null -eq $dev) {
      $ioRoot = Lookup-OrNull $sysman "TIID"
      $dev = Find-IoRootChildByNameAndSubtype -ioRoot $ioRoot -name $deviceName -subType 111
    }
    if ($null -ne $dev -and -not [string]::IsNullOrWhiteSpace($leafName)) {
      foreach ($it in Get-TreeItemsDepthFirst $dev 120000) {
        $nm = Get-ItemNameSafe $it ""
        if ($nm -ieq $leafName -or $nm -ieq "DevState" -or $nm -ieq "FrmDevState") {
          $ioItem = $it
          break
        }
      }
    }
  }
  if ($null -eq $ioItem) {
    Write-Warning ("Pre-Compile Master-Link uebersprungen: IO-Item nicht gefunden: {0}" -f $ioPrePath)
    return
  }

  $pattern = "(^|\\.)SlaveCount$|fbEthercat\\.SlaveCount$"
  try {
    if ($cfg.twincat.PSObject.Properties.Name -contains "preCompileMasterVarPattern") {
      $pat = [string]$cfg.twincat.preCompileMasterVarPattern
      if (-not [string]::IsNullOrWhiteSpace($pat)) { $pattern = $pat }
    }
  } catch {}
  $rx = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

  $plcVar = $null
  $instRoots = Get-PlcInstanceRoots -sysman $sysman -plcProjectName $plcProjectName
  foreach ($inst in $instRoots) {
    foreach ($it in Get-TreeItemsDepthFirst $inst 300000) {
      $nm = Get-ItemNameSafe $it ""
      if ([string]::IsNullOrWhiteSpace($nm)) { continue }
      if ($rx.IsMatch($nm)) { $plcVar = $it; break }
    }
    if ($null -ne $plcVar) { break }
  }
  if ($null -eq $plcVar) {
    Write-Warning ("Pre-Compile Master-Link uebersprungen: PLC-Variablenmuster nicht gefunden: {0}" -f $pattern)
    return
  }

  Invoke-WithComRetry -Action { $sysman.LinkVariables([string]$plcVar.PathName, [string]$ioItem.PathName, 0, 0, 0) } | Out-Null
  Write-Info ("Pre-Compile Master-Link erstellt: {0} <-> {1}" -f [string]$plcVar.PathName, [string]$ioItem.PathName)
}

function Link-All($sysman, $cfg) {
  $deviceName = $cfg.twincat.deviceName
  $plcProjectName = $cfg.twincat.plcProjectName
  $gvlName = $cfg.twincat.gvlName

  $results = @()
  $skipped = @()

  foreach ($p in $cfg.ioPoints) {
    $dir = ""
    try {
      $dir = ([string]$p.direction).ToUpperInvariant().Trim()
      if ([string]::IsNullOrWhiteSpace($dir)) { throw "Richtung fehlt" }

      $ioLeafOverride = if ($p.PSObject.Properties.Name -contains "ioLeaf") { [string]$p.ioLeaf } else { "" }
      $plcPathUsed = ""
      $ioPathUsed = ""
      $linked = $false

      $ioPathCandidates = Get-IoLeafPathCandidatesFromSchema -terminalPathsIndex $script:__SchemaTerminalPaths -terminalName ([string]$p.terminalName) -channel ([int]$p.channel) -direction $dir
      foreach ($ioc in $ioPathCandidates) {
        $plcPathUsed = Try-LinkByDirectPlcPath -sysman $script:__TcSysMan -plcProjectName $plcProjectName -gvlName $gvlName -varName ([string]$p.varName) -direction $dir -ioPath ([string]$ioc)
        if (-not [string]::IsNullOrWhiteSpace($plcPathUsed)) {
          $ioPathUsed = [string]$ioc
          $linked = $true
          break
        }
      }

      if (-not $linked) {
        $term = Resolve-TerminalBySchemaPath -sysman $script:__TcSysMan -terminalPathsIndex $script:__SchemaTerminalPaths -terminalName ([string]$p.terminalName)
        $ioLeaf = Resolve-IoLeafBySchemaPath -sysman $script:__TcSysMan -terminalPathsIndex $script:__SchemaTerminalPaths -terminalName ([string]$p.terminalName) -channel ([int]$p.channel) -direction $dir
        if ($null -eq $ioLeaf) {
          if ($null -eq $term) {
            $term = Invoke-WithComRetry -Action { Find-TerminalItem -sysman $script:__TcSysMan -deviceName $deviceName -terminalName $p.terminalName }
          }
          $ioLeaf = Invoke-WithComRetry -Action { Find-IoLeaf -terminalItem $term -channel ([int]$p.channel) -direction $dir -ioLeafOverride $ioLeafOverride }
        }

        $plcVar = Invoke-WithComRetry -Action { Find-PlcVarItem -sysman $script:__TcSysMan -plcProjectName $plcProjectName -gvlName $gvlName -varName $p.varName -direction $dir }
        if ($null -eq $plcVar) {
          $plcPathUsed = Try-LinkByDirectPlcPath -sysman $script:__TcSysMan -plcProjectName $plcProjectName -gvlName $gvlName -varName ([string]$p.varName) -direction $dir -ioPath ([string]$ioLeaf.PathName)
          if ([string]::IsNullOrWhiteSpace($plcPathUsed)) {
            throw ("PLC-Variable nicht im Process Image gefunden: {0}.{1}" -f $gvlName, $p.varName)
          }
          $ioPathUsed = [string]$ioLeaf.PathName
        } else {
          Invoke-WithComRetry -Action { $script:__TcSysMan.LinkVariables([string]$plcVar.PathName, [string]$ioLeaf.PathName, 0, 0, 0) } | Out-Null
          try { $plcPathUsed = [string]$plcVar.PathName } catch {}
          try { $ioPathUsed = [string]$ioLeaf.PathName } catch {}
        }
      }
      Write-Ok ("Linked: {0} <-> {1}" -f $p.varName, $ioPathUsed)

      $results += [pscustomobject]@{
        varName     = $p.varName
        direction   = $dir
        type        = $p.type
        terminal    = $p.terminalName
        channel     = [int]$p.channel
        group       = $p.group
        description = $p.description
        plcPath     = $plcPathUsed
        ioPath      = $ioPathUsed
      }
    } catch {
      $errMsg = $_.Exception.Message
      $msg = ("Link uebersprungen: var='{0}' term='{1}' ch='{2}' dir='{3}' :: {4}" -f $p.varName, $p.terminalName, $p.channel, $dir, $errMsg)
      if ($cfg.twincat.linking.failIfUnresolved) { throw $msg }
      Write-Warning $msg

      $skipped += [pscustomobject]@{
        varName   = $p.varName
        terminal  = $p.terminalName
        channel   = [int]$p.channel
        direction = $dir
        error     = $errMsg
      }
      continue
    }
  }

  Write-Info ("Link-Zusammenfassung: OK={0}  Uebersprungen={1}" -f $results.Count, $skipped.Count)
  $script:__LinkSkipped = $skipped
  return $results
}

# -------------------- Main --------------------

try {
  Write-Info ("Apply-PtcXaeMcp_PS51 build: {0}" -f $script:ScriptBuild)
  if (!(Test-Path -LiteralPath $SchemaJson)) { throw "JSON nicht gefunden: $SchemaJson" }
  Write-Info "Lese Schema-JSON: $SchemaJson"
  $cfg = $null
  try {
    $cfg = ConvertFrom-JsonCompat -Json (Get-Content -LiteralPath $SchemaJson -Raw -Encoding UTF8) -Depth 100
  } catch {
    throw ("JSON-Syntaxfehler in '{0}': {1}" -f $SchemaJson, $_.Exception.Message)
  }
  Validate-SchemaConfig -cfg $cfg
  Show-RunConfiguration -SchemaPath $SchemaJson -SkipHw ([bool]$NoHw) -SkipGvl ([bool]$NoGvl) -SkipLink ([bool]$NoLink) -SkipExport ([bool]$NoExport) -AutoBuildConfirm ([bool]$AutoConfirmBuild)

  if ($null -eq $cfg.twincat) { throw "Ungueltiges JSON: Root 'twincat' fehlt" }
  if ($null -eq $cfg.ethercat -or $null -eq $cfg.ethercat.root) { throw "Ungueltiges JSON: 'ethercat.root' fehlt" }
  $script:__SchemaTerminalPaths = Build-SchemaTerminalPathIndex -cfg $cfg

  Write-Info "Verbinde mit TwinCAT XAE..."
  $dte = Get-ActiveTcDte
  $sysman = Get-SystemManagerFromSolution $dte
  $script:__TcDte = $dte
  $script:__TcSysMan = $sysman
  $ioRoot = $sysman.LookupTreeItem("TIID")

  $resolvedPlcName = Resolve-PlcProjectName -sysman $sysman -requestedName ([string]$cfg.twincat.plcProjectName)
  if (-not [string]::IsNullOrWhiteSpace($resolvedPlcName) -and $resolvedPlcName -ne [string]$cfg.twincat.plcProjectName) {
    Write-Info ("PLC-Projekt aufgeloest: '{0}' -> '{1}'" -f [string]$cfg.twincat.plcProjectName, $resolvedPlcName)
    $cfg.twincat.plcProjectName = $resolvedPlcName
  }

  if (-not $NoHw) {
    Write-Step "EtherCAT Tree"
    Apply-EthercatTree -sysman $sysman -cfg $cfg
  } else {
    Write-Info "Schritt uebersprungen: EtherCAT Tree (-NoHw)"
  }

  if (-not $NoGvl) {
    Write-Step ("GVL '{0}'" -f $cfg.twincat.gvlName)
    $gvlText = Render-GvlText -gvlName $cfg.twincat.gvlName -ioPoints $cfg.ioPoints
    $script:__LastGvlItem = Ensure-Gvl -sysman $sysman -plcProjectName $cfg.twincat.plcProjectName -gvlName $cfg.twincat.gvlName -declText $gvlText
    if ($cfg.twincat.linking.enabled) {
      if (-not $NoLink) {
        $autoMasterLink = $false
        try {
          if ($cfg.twincat.PSObject.Properties.Name -contains "autoMasterLink") {
            $autoMasterLink = [bool]$cfg.twincat.autoMasterLink
          }
        } catch {}
        if ($autoMasterLink) {
          Apply-PreCompileMasterLink -sysman $script:__TcSysMan -cfg $cfg
        }
      }
      # Always refresh process image after GVL changes so symbols are available in a later -NoGvl link pass.
      Refresh-PlcProcessImage -dte $script:__TcDte
    }
  } else {
    Write-Info "Schritt uebersprungen: GVL (-NoGvl)"
  }

  $linkResults = @()
  if (-not $NoLink -and $cfg.twincat.linking.enabled) {
    if ($NoGvl) {
      # In a split run (-NoHw -NoLink, then -NoHw -NoGvl), ensure instance/process-image symbols are up to date.
      Refresh-PlcProcessImage -dte $script:__TcDte
    }
    Write-Step ("Linking (failIfUnresolved={0})" -f $cfg.twincat.linking.failIfUnresolved)
    $linkResults = Link-All -sysman $sysman -cfg $cfg
  } else {
    Write-Info "Schritt uebersprungen: Linking (-NoLink oder linking.disabled)"
  }

  if (-not $NoExport) {
    Write-Step "Export"
    $xtiPath = $cfg.twincat.export.xtiPath
    $csvPath = $cfg.twincat.export.mappingCsvPath

    if ($xtiPath) {
      Ensure-OutputFolder $xtiPath
      Write-Info "Exportiere XTI: $xtiPath"
      $ioRoot.ExportChild($cfg.twincat.deviceName, $xtiPath)
    }

    if ($csvPath) {
      Ensure-OutputFolder $csvPath
      if ($linkResults.Count -gt 0) {
        Write-Info "Schreibe Mapping-CSV: $csvPath"
        $linkResults | Export-Csv -NoTypeInformation -Delimiter ';' -Encoding UTF8 -Path $csvPath
      } else {
        Write-Warning "mappingCsvPath gesetzt, aber keine Links erstellt (0). CSV wird nicht geschrieben."
      }

      if ($script:__LinkSkipped -and $script:__LinkSkipped.Count -gt 0) {
        $skippedCsv = [System.IO.Path]::Combine((Split-Path -Parent $csvPath), ([System.IO.Path]::GetFileNameWithoutExtension($csvPath) + "_skipped.csv"))
        Write-Info "Schreibe CSV mit uebersprungenen Links: $skippedCsv"
        $script:__LinkSkipped | Export-Csv -NoTypeInformation -Delimiter ';' -Encoding UTF8 -Path $skippedCsv
      }
    }
  } else {
    Write-Info "Schritt uebersprungen: Export (-NoExport)"
  }

  Write-Ok "Fertig."
} catch {
  Write-ErrLine $_.Exception.Message
  Show-ErrorGuidance -errorRecord $_ -schemaPath $SchemaJson
  if ($_.InvocationInfo) {
    $ln = $_.InvocationInfo.ScriptLineNumber
    $line = ($_.InvocationInfo.Line | Out-String).Trim()
    if ($line) { Write-ErrLine ("In Zeile {0}: {1}" -f $ln, $line) } else { Write-ErrLine ("In Zeile {0}" -f $ln) }
  }
  if ($_.ScriptStackTrace) { Write-Host $_.ScriptStackTrace }
  exit 1
}
