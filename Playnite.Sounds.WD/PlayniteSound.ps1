$global:players = @{}
$global:DoCheck = $True

function PlayFileName {
	param (
		$FileName
	)	
	
	#must be done here and only once
	#so on first try of audio play on first run files will be downloaded
	if ($global:DoCheck)
	{
		$global:DoCheck = $False;
		$ExtensionDataPath = Join-Path -Path $PlayniteApi.Paths.ExtensionsDataPath -ChildPath "Playnite.Sounds.WD"
		$SoundFilesDataPath = Join-Path -Path $ExtensionDataPath -ChildPath "Sound Files"
		$SettingsDataPath = Join-Path -Path $ExtensionDataPath -ChildPath "Settings" 
		
		$FirstRunPath = Join-Path -Path $SettingsDataPath -ChildPath "firstrundone.dat"
	
		if ((Test-Path $FirstRunPath) -eq $False)
		{
			New-Item -ItemType Directory -Path $ExtensionDataPath -Force
			New-Item -ItemType Directory -Path $SoundFilesDataPath -Force
			New-Item -ItemType Directory -Path $SettingsDataPath -Force
		
			$__logger.Info("First Run of extensions detected")
			$TempDownLoadPath = Join-Path -Path $env:TEMP -ChildPath "Playnite.Sounds.WD.data.zip"
			
			try {
				$Uri = "https://github.com/joyrider3774/PlayniteSound/raw/main/data/Sound%20Files.zip"
				$__logger.Info("Downloading " + $Uri)
				
				Invoke-WebRequest $Uri -OutFile $TempDownloadPath
				$__logger.Info("Audio files succesfully downloaded...")
				
				Expand-Archive -LiteralPath $TempDownloadPath -DestinationPath $ExtensionDataPath -Force
				$__logger.Info("Audio Files extracted to " + $ExtensionDataPath)
				New-Item -ItemType "file" $FirstRunPath
				
				$MsgBoxResult = $PlayniteApi.Dialogs.ShowMessage( 
"Welcome to Playnite Sounds.
				
logger Mockup audio files have been succesfully downloaded.
After making your choice below, the first audio files should start playing if all goes fine

This extension can play wav files when PlayNite events happen...
Files starting with D_* are audio files played in desktop mode and F_* in fullscreenmode.
You can delete any wav audio files, if you do not want to hear certain sounds, or you can replace them all.
Do note you will have to restart the extension or PlayNite before in order for new audio files to be loaded.

Do you want to open the folder containing the files ?",
				"Playnite Sounds First Run",
				"YesNo")
				
				if($MsgBoxResult -eq "Yes")
				{
					Invoke-Item $SoundFilesDataPath
				}
				
				#reset loaded players so audio files get reloaded
				$global:players = @{}
			}
			catch {
				$ErrorMessage = $_.Exception.Message
				$__logger.Error("Error downloading Audio Files during initialial setup. Error: " + $ErrorMessage)
				$PlayniteApi.Dialogs.ShowErrorMessage("Error downloading audio files", $ErrorMessage);
			}
		}
	}
	
	if ($global:players.ContainsKey($FileName))
	{
		$Entry = $global:players[$FileName]
	}
	else
	{
		if ($PlayniteApi.ApplicationInfo.Mode -eq "Desktop") {
			$Prefix = "D_"
		}
		else
		{
			$Prefix = "F_"
		}
		
		$FullFileName = Join-Path -Path $PlayniteApi.Paths.ExtensionsDataPath -ChildPath "Playnite.Sounds.WD" 
		$FullFileName = Join-Path -Path $FullFileName -ChildPath "Sound Files"
		$FullFileName = Join-Path -Path $FullFileName -ChildPath ($Prefix + $FileName)
		
		#MediaPlayer can play multiple sounds together from mulitple instances SoundPlayer can not
		#$Entry = @((Test-Path $FullFileName), (New-Object -TypeName System.Media.SoundPlayer))
	
		$Entry = @((Test-Path $FullFileName), (New-Object -TypeName System.Windows.Media.MediaPlayer))
		
		#$Entry = @((Test-Path $FullFileName), (New-Object -TypeName System.Windows.Controls.MediaElement))

		if($Entry[0])
		{
			$Entry[1].Open([System.Uri]$FullFileName) #System.Windows.Media.MediaPlayer
		#	$Entry[1].SoundLocation = $FullFileName #System.Media.SoundPlayer
		#	$Entry[1].Source = $FullFileName      #System.Windows.Controls.MediaElement
		#	$Entry[1].UnloadedBehavior = "Manual" #System.Windows.Controls.MediaElement
		}
		$global:players[$FileName] = $Entry 
		
	}
		
	if ($Entry[0])
	{
		$Entry[1].Stop() 
		$Entry[1].Play()
	}
}

function global:OnApplicationStarted()
{	
	PlayFileName "ApplicationStarted.wav"
}

function global:OnApplicationStopped()
{
	PlayFileName "ApplicationStopped.wav"
}

function global:OnLibraryUpdated()
{
	PlayFileName "LibraryUpdated.wav"
}

function global:OnGameStarting()
{
    param(
        $game
    )

	PlayFileName "GameStarting.wav"
}

function global:OnGameStarted()
{
    param(
        $game
    )

	PlayFileName  "GameStarted.wav"
}

function global:OnGameStopped()
{
    param(
        $game,
        $elapsedSeconds
    )

	PlayFileName "GameStopped.wav"
}

function global:OnGameInstalled()
{
    param(
        $game
    )

	PlayFileName "GameInstalled.wav"
}

function global:OnGameUninstalled()
{
    param(
        $game
    )

	PlayFileName "GameUninstalled.wav"
}

function global:OnGameSelected()
{
    param(
        $args
    )
	
	PlayFileName "GameSelected.wav"
}