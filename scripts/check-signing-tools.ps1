[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
$makeAppx = Get-Command makeappx.exe -ErrorAction SilentlyContinue

[pscustomobject]@{
    SignTool = if ($signTool) { $signTool.Source } else { $null }
    MakeAppx = if ($makeAppx) { $makeAppx.Source } else { $null }
}

$stores = "Cert:\CurrentUser\My", "Cert:\LocalMachine\My"
foreach ($store in $stores) {
    Get-ChildItem -LiteralPath $store -ErrorAction SilentlyContinue |
        Where-Object {
            $_.EnhancedKeyUsageList.FriendlyName -contains "Code Signing" -or
            $_.EnhancedKeyUsageList.ObjectId -contains "1.3.6.1.5.5.7.3.3"
        } |
        Select-Object @{Name = "Store"; Expression = { $store } }, Subject, Thumbprint, NotAfter, HasPrivateKey
}
