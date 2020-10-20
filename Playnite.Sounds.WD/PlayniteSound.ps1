Add-Type -AssemblyName PresentationCore

$global:players = @{}
$global:closeaudiofilesnextplay = $False

function global:GetMainMenuItems() {
	param($menuArgs)

	$menuItem1 = New-Object Playnite.SDK.Plugins.ScriptMainMenuItem
	$menuItem1.Description = "Reload Audio Files"
	$menuItem1.FunctionName = "ReloadAudioFilesMenu"
	$menuItem1.MenuSection = "@Playnite Sounds"

	$menuItem2 = New-Object Playnite.SDK.Plugins.ScriptMainMenuItem
	$menuItem2.Description = "Open Audio Files Folder"
	$menuItem2.FunctionName = "OpenSoundsFolderMenu"
	$menuItem2.MenuSection = "@Playnite Sounds"

	$menuItem3 = New-Object Playnite.SDK.Plugins.ScriptMainMenuItem
	$menuItem3.Description = "Audio Files Help"
	$menuItem3.FunctionName = "HelpMenu"
	$menuItem3.MenuSection = "@Playnite Sounds"

	return $menuItem1, $menuItem2, $menuItem3
}

function global:HelpMenu() {
	$PlayniteApi.Dialogs.ShowMessage("Playnite Sounds is an extension to play audio files during Playnite events. `r`n`r`n" +
		"It can only play WAV audio files and nothing else.`r`n" +
		"There are 2 seperate set of sound files. Audio Files starting with 'D_' and files starting with 'F_'. " +
		"The 'D_' files are the audio files for desktop mode, the 'F_' files for fullscreen mode.`r`n`r`n" +
		"If you don't want to hear a certain file you can just delete the wav file of the event you don't want to hear. " +
		"You can also change the files with your own files. Be sure to use the 'Open Audio Files Folder' menu for doing so. " +
		"It will make sure loaded audio files get closed so you can overwrite them. Make sure playnite does not play any audio " +
		"anymore after opening the folder or it's possible you can't overwrite that specific file. With my testings it did seem " +
		"you could still first erase the files. After changing the audio files use the 'Reload Audio Files' menu to clear any loaded " +
		"files and use your new files, or just restart Playnite. " +
		"Do NOT use a long audio file for ApplicationStopped as Playnite will not quit until that audio file has finished playing. " +
		"The same applies for switching between desktop and fullscreen mode.`r`n`r`n" +
		"These are the audio files that can be used`r`n`r`n" +
		"D_ApplicationStarted.wav - F_ApplicationStarted.wav`r`n" +
		"D_ApplicationStopped.wav - F_ApplicationStopped.wav`r`n" +
		"D_GameInstalled.wav - F_GameInstalled.wav`r`n" +
		"D_GameSelected.wav - F_GameSelected.wav`r`n" +
		"D_GameStarted.wav - F_GameStarted.wav`r`n" +
		"D_GameStarting.wav - F_GameStarting.wav`r`n" +
		"D_GameStopped.wav - F_GameStopped.wav`r`n" +
		"D_GameUninstalled.wav - F_GameUninstalled.wav`r`n" +
		"D_LibraryUpdated.wav - F_LibraryUpdated.wav")
}

function global:OpenSoundsFolderMenu() {
	#need to release them otherwise explorer can't overwrite files even though you can delete them
	CloseAudioFiles
	$SoundFilesDataPath = Join-Path -Path $CurrentExtensionInstallPath -ChildPath "Sound Files"

	#just in case user deleted it
	New-Item -ItemType Directory -Path $SoundFilesDataPath -Force
	Invoke-Item $SoundFilesDataPath
}

function global:ReloadAudioFilesMenu() {
	CloseAudioFiles
	$PlayniteApi.Dialogs.ShowMessage("Audio files reloaded!", "Audio files reinitialized")
}

function global:CloseAudioFiles() {

	foreach ($key in $global:players.keys)
	{
		$Entry = $global:players[$key]
		$Entry[1].Stop()
		if ($Entry[2] -eq 1) {
			$Entry[1].Close() #mediaplayer
			$Entry[1] = $null
		}
	}
	$global:players = @{}
}

function global:PlayFileName() {
	param (
		$FileName,
		$UseSoundPlayer = $False
	)

	if ($global:closeaudiofilesnextplay)
	{
		CloseAudioFiles
		$global:closeaudiofilesnextplay = $False
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

		$FullFileName = Join-Path -Path $CurrentExtensionInstallPath -ChildPath "Sound Files" |
			Join-Path -ChildPath ($Prefix + $FileName) 

		#MediaPlayer can play multiple sounds together from mulitple instances SoundPlayer can not
		if($UseSoundPlayer) {
			$Entry = @((Test-Path $FullFileName), (New-Object -TypeName System.Media.SoundPlayer), 0)
		}
		else
		{
			$Entry = @((Test-Path $FullFileName), (New-Object -TypeName System.Windows.Media.MediaPlayer), 1)
		}

		#$Entry = @((Test-Path $FullFileName), (New-Object -TypeName System.Windows.Controls.MediaElement), 2)

		if($Entry[0])
		{
			if ($Entry[2] -eq 1) {
				$Entry[1].Open([System.Uri]$FullFileName) #System.Windows.Media.MediaPlayer
			}
			if ($Entry[2] -eq 0) {
				$Entry[1].SoundLocation = $FullFileName #System.Media.SoundPlayer
				$Entry[1].Load()
			}
			#$Entry[1].Source = $FullFileName      #System.Windows.Controls.MediaElement
			#$Entry[1].UnloadedBehavior = "Manual" #System.Windows.Controls.MediaElement
		}
		$global:players[$FileName] = $Entry 

	}

	if ($Entry[0])
	{
		$Entry[1].Stop() 
		if ($Entry[2] -eq 1)
		{
			$Entry[1].Play()
		}
		else
		{
			$Entry[1].PlaySync()
		}
	}
}

function StartSystemEvents {
	$null = Register-ObjectEvent -InputObject ([Microsoft.Win32.SystemEvents]) -EventName "PowerModeChanged" -Action {
		if ($Event.SourceEventArgs.Mode -eq 'Resume')
		{
			$global:closeaudiofilesnextplay = $True
		}
	}
}

function StopSystemEvents {
	$events = Get-EventSubscriber | Where-Object { $_.SourceObject -eq [Microsoft.Win32.SystemEvents] } 
	$jobs = $events | Select-Object -ExpandProperty Action
	$events | Unregister-Event
	$jobs | Remove-Job
}

function global:OnApplicationStarted()
{
	StartSystemEvents
	PlayFileName "ApplicationStarted.wav"
}

function global:OnApplicationStopped()
{
	#last parameter makes it use soundplayer and played synced 
	#so that the audio file is fully played before quiting
	PlayFileName "ApplicationStopped.wav" $True
	#need to release them here otherwise we might have problems
	#switching to fullscreen.
	CloseAudioFiles
	StopSystemEvents
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

	PlayFileName "GameStarted.wav"
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