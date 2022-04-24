using namespace System
using namespace System.Collections.Generic

param(
    [switch] $UpdateXaml,
    [switch] $GenerateResource,
    $RootDir = "..\"
)

$LocalizationDir = $rootDir + "Localization\"
$SourceLocalFileName = "LocSource.xaml"
$SourceLocalFilePath = $LocalizationDir + $SourceLocalFileName
$ResourceFilePath = $rootDir + "Common\Constants\Resource.cs"
$SourceLines = Get-Content -Path $SourceLocalFilePath
$SourceLines = [List[string]]$SourceLines

function Get-IndexKey {
    param (
        [List[string]] $lines
    )
    $keys = New-Object 'Collections.Generic.List[Tuple[int,string]]'

    for ($i = 0; $i -lt $lines.Count; $i++)
    {
        $str = $lines[$i]
        if ($str -match "<sys:String x:Key=`"(.*)`">")
        {
            $keys.Add([Tuple]::Create($i, $Matches[1]))
        }
    }

    return $keys
}

function Add-LineIntoFile {
    param (
        [List[string]] $lines,
        [string] $line,
        [int] $index
    )
    $location = $index - 1
    [List[String]] $newLines = $lines[0..$location]

    $newLines.Add($line)

    $end = $lines.Count - 1
    $newLines.AddRange([List[String]]$lines[$index..$end])

    return $newLines
}


function Set-XamlFiles {
    $sourceKeys = Get-IndexKey -lines $SourceLines

    $files = Get-ChildItem $LocalizationDir
    $files = $files | Where-Object {$_ -notmatch $SourceLocalFileName}

    foreach ($file in $files)
    {
    $fileContent = Get-Content -Path $file
    [List[Tuple[int,string]]] $fileKeys = Get-IndexKey -lines $fileContent
    foreach($keyTuple in $sourceKeys)
    {
        if ($fileKeys.Where({$_.Item2 -eq $keyTuple.Item2}).Count -eq 0)
        {
            $fileContent = Add-LineIntoFile -lines $fileContent -line $SourceLines[$keyTuple.Item1] -index $keyTuple.Item1
        }
    }

    Set-Content -Path $file -Value $fileContent
    }
}

function New-Resource {
    $start = $SourceLines.FindLastIndex({$args[0] -match "<!-- Settings Resources -->"});
    if ($start -eq -1)
    {
        return
    }

    $resourceLines = [List[String]]@(
        "using Playnite.SDK;`r",
        "using System;`r",
        "`r", 
        "namespace PlayniteSounds.Common.Constants`r", 
        "{`r",
        "    public class Resource`r", 
        "    {`r"
    )

    for ($i = $start; $i -lt $SourceLines.Count; $i++)
    {
        $line = $SourceLines[$i];
        if ($line -match "<sys:String x:Key=`"(LOC_PLAYNITESOUNDS_(.*))`">")
        {
            $resourceId = $Matches[1]
            $varName = $Matches[2]
            $lazyVarName = "_" + $varName.substring(0,1).tolower() + $varName.substring(1)
            $resourceLines.Add("        public static string ${varName} => ${lazyVarName}.Value;`r")
            $resourceLines.Add("        private static readonly Lazy<string> ${lazyVarName} = new Lazy<string>(() => ToId(`"${resourceId}`"));`r")
            $resourceLines.Add("`r");
        }
    }

    $resourceLines.Add([List[String]]@(
        "        private static string ToId(string id) => ResourceProvider.GetString(id);`r",
        "    }`r",
        "}`r"
    ))

    Set-Content -Path $ResourceFilePath -Value $resourceLines -NoNewline
}


function Main {
    if ($UpdateXaml)
    {
        Set-XamlFiles
    }

    if ($GenerateResource)
    {
        New-Resource
    }
}

Main