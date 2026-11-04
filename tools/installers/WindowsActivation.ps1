# Assert decoder completion, as well as a real associated-file window.
function Test-AssociatedArchiveLoaded([string[]]$Logs, [DateTimeOffset]$Since, [string]$Archive) {
    foreach($log in $Logs) {
        if(-not (Test-Path $log)){continue}
        foreach($line in (Get-Content -Tail 12 $log)) {
            try {$record=$line | ConvertFrom-Json} catch {continue} # Writer may be appending.
            if($record.action -eq 'file-activation' -and [DateTimeOffset]$record.timestamp -ge $Since -and
               $record.detail -match ('success=True; archive='+[regex]::Escape($Archive)+'; entries=[1-9][0-9]*$')) {return $true}
        }
    }
    return $false
}
