function get_target_vs_versions($vstarget, $vsroot) {
    if ($vstarget -eq "15.0") {
        if (-not $vsroot) {
            Throw "The -vsroot argument must be specified for VS 2017"
        } else {
            return get_target_vs15_version $vsroot
        }
    }

    $supported_vs_versions = (
        @{number="15.0"; name="VS 2017"; build_by_default=$true},
        @{number="14.0"; name="VS 2015"; build_by_default=$true}
    )

    $target_versions = @()

    if ($vstarget) {
        $vstarget = "{0:00.0}" -f [float]::Parse($vstarget)
    }
    foreach ($target_vs in $supported_vs_versions) {
        if ((-not $vstarget -and $target_vs.build_by_default) -or ($target_vs.number -in $vstarget)) {
            # Note: These registry entries are not present for "15.0", thus it won't be built.
            $vspath = Get-ItemProperty -Path "HKLM:\Software\Wow6432Node\Microsoft\VisualStudio\$($target_vs.number)" -EA 0
            if (-not $vspath) {
                $vspath = Get-ItemProperty -Path "HKLM:\Software\Microsoft\VisualStudio\$($target_vs.number)" -EA 0
            }
            if ($vspath -and $vspath.InstallDir -and (Test-Path -Path $vspath.InstallDir)) {
                $msbuildroot = "${env:ProgramFiles(x86)}\MSBuild\Microsoft\VisualStudio\v$($vstarget)"
                $target_versions += @{
                    number=$target_vs.number;
                    name=$target_vs.name;
                    vsroot="$($vspath.InstallDir)..\..\";
                    msbuildroot=$msbuildroot
                }
            }
        }
    }
    
    if ($vstarget.Count -gt $target_versions.Count) {
        Write-Warning "Not all specified VS versions are available. Targeting only $($target_versions | %{$_.number})"
    }
    
    if (-not $target_versions) {
        Throw "No supported versions of Visual Studio installed."
    }
    
    return $target_versions
}

function get_target_vs15_version($vsroot) {
    $msbuildroot="${vsroot}\MSBuild\Microsoft\VisualStudio\v15.0"
    return @{
        number="15.0";
        name="VS 2017";
        vsroot=$vsroot;
        msbuildroot=$msbuildroot
    }; 
}