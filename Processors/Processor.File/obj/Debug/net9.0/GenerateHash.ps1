$fileContent = Get-Content -Path 'FileProcessorApplication.cs' -Raw -Encoding UTF8
$hasher = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $hasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($fileContent))
$hash = [System.BitConverter]::ToString($hashBytes) -replace '-', ''
Write-Output $hash.ToLower()
