$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$sourcePath = Join-Path $projectDir 'src\PcCareUniversal.cs'
$iconPath = Join-Path $projectDir 'assets\pc-care.ico'
$imagePath = Join-Path $projectDir 'assets\pc-care-icon.png'
$manifestPath = Join-Path $projectDir 'app.manifest'
$buildDir = Join-Path $projectDir 'build'
$distDir = Join-Path $projectDir 'dist'
$verifyDir = Join-Path $projectDir '검증'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$version = '3.1.0'

foreach ($folder in @($buildDir, $distDir, $verifyDir)) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# 컴파일러를 찾지 못했습니다: $compiler"
}

$profiles = @(
    [pscustomobject]@{ Key = '자동감지'; Defines = 'THEONE'; File = "더원_PC_케어_자동감지_v$version.exe" },
    [pscustomobject]@{ Key = '노트북'; Defines = 'THEONE,LAPTOP'; File = "더원_PC_케어_노트북_v$version.exe" },
    [pscustomobject]@{ Key = '데스크톱'; Defines = 'THEONE,DESKTOP'; File = "더원_PC_케어_데스크톱_v$version.exe" }
)

$results = @()
foreach ($profile in $profiles) {
    $buildExe = Join-Path $buildDir $profile.File
    $distExe = Join-Path $distDir $profile.File
    $profileVerify = Join-Path $verifyDir $profile.Key
    New-Item -ItemType Directory -Force -Path $profileVerify | Out-Null

    $compilerArgs = @(
        '/nologo'
        '/target:winexe'
        '/platform:anycpu'
        '/optimize+'
        "/define:$($profile.Defines)"
        "/win32icon:$iconPath"
        "/win32manifest:$manifestPath"
        "/resource:$imagePath,PcCareIcon"
        "/out:$buildExe"
        '/reference:System.dll'
        '/reference:System.Core.dll'
        '/reference:System.Drawing.dll'
        '/reference:System.Windows.Forms.dll'
        '/reference:System.ServiceProcess.dll'
        '/reference:System.Management.dll'
        $sourcePath
    )

    & $compiler @compilerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "$($profile.Key) 컴파일 실패: 종료 코드 $LASTEXITCODE"
    }
    Copy-Item -LiteralPath $buildExe -Destination $distExe -Force

    $selfTest = Join-Path $profileVerify '자체진단.txt'
    $plan = Join-Path $profileVerify '조치계획.txt'
    $dashboard = Join-Path $profileVerify '대시보드.png'

    $process = Start-Process -FilePath $distExe -ArgumentList @('--self-test', $selfTest) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "$($profile.Key) 자체진단 실패: $($process.ExitCode)" }
    $process = Start-Process -FilePath $distExe -ArgumentList @('--office-plan', $plan) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "$($profile.Key) 조치계획 실패: $($process.ExitCode)" }
    $process = Start-Process -FilePath $distExe -ArgumentList @('--ui-snapshot', $dashboard, 'warning') -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "$($profile.Key) 화면 검증 실패: $($process.ExitCode)" }

    $file = Get-Item -LiteralPath $distExe
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($distExe)
    $results += [pscustomobject]@{
        Profile = $profile.Key
        File = $profile.File
        Bytes = $file.Length
        ProductVersion = $fileVersion.ProductVersion
        SHA256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $distExe).Hash
        Signature = (Get-AuthenticodeSignature -LiteralPath $distExe).Status
        SelfTest = 'PASS'
        Plan = 'PASS'
        Render = 'PASS'
    }
}

$results | Export-Csv -LiteralPath (Join-Path $verifyDir '파일검증.csv') -NoTypeInformation -Encoding UTF8
$checksumLines = $results | ForEach-Object { "$($_.SHA256) *$($_.File)" }
[System.IO.File]::WriteAllLines((Join-Path $distDir 'SHA256SUMS.txt'), $checksumLines, (New-Object System.Text.UTF8Encoding($false)))
$report = @(
    '더원 PC 케어 Universal 최종검증'
    ('검증 시각: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    ('검증 컴퓨터: ' + $env:COMPUTERNAME)
    '사용자 파일 삭제 기능: 없음'
    '상시 위젯: 없음'
    '실제 서비스 정지·탐색기·OneDrive·STT 조치: 빌드 검증에서는 실행하지 않음'
    ''
)
foreach ($row in $results) {
    $report += "[$($row.Profile)] $($row.File)"
    $report += "  크기: $($row.Bytes) bytes"
    $report += "  버전: $($row.ProductVersion)"
    $report += "  SHA-256: $($row.SHA256)"
    $report += "  전자서명: $($row.Signature)"
    $report += "  자체진단/계획/화면: PASS/PASS/PASS"
}
[System.IO.File]::WriteAllLines((Join-Path $verifyDir '최종검증보고서.txt'), $report, (New-Object System.Text.UTF8Encoding($true)))

Write-Host ''
Write-Host '세 프로필 빌드와 비파괴 검증이 완료됐습니다.' -ForegroundColor Green
$results | Format-Table Profile,File,Bytes,ProductVersion,SelfTest,Render -AutoSize

