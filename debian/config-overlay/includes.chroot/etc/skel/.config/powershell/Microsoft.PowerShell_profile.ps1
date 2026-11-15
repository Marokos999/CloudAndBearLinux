# CloudAndBear: PowerShell profil (isti kao na Windowsu: oh-my-posh tema i PSReadLine predlozi)
# Konzola laptopa (TERM=linux) ne zna ikone, pa dobija varijantu teme bez njih; SSH iz Windows Terminala dobija punu temu.
if (Get-Command oh-my-posh -ErrorAction SilentlyContinue) {
    $theme = if ($env:TERM -eq 'linux') { 'minimal-marko-tty' } else { 'minimal-marko' }
    oh-my-posh init pwsh --config "/usr/share/cloudandbear/oh-my-posh/$theme.omp.json" | Invoke-Expression
}

Set-PSReadLineOption -PredictionSource HistoryAndPlugin
Set-PSReadLineOption -PredictionViewStyle ListView
Set-PSReadLineOption -EditMode Windows
Set-PSReadLineKeyHandler -Key Tab -Function MenuComplete
Set-PSReadLineKeyHandler -Key UpArrow -Function HistorySearchBackward
Set-PSReadLineKeyHandler -Key DownArrow -Function HistorySearchForward
