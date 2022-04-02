# Playnite Sounds Extension
Playnite Sounds is an extension to play audio files during Playnite events. 
It can only play WAV audio files and mp3 for music, nothing else.

[Latest Release](https://github.com/joyrider3774/PlayniteSound/releases/latest)

## Buy me a "koffie" if you feel like supporting 
I do everything in my spare time for free, if you feel something aided you and you want to support me, you can always buy me a "koffie" as we say in dutch, no obligations whatsoever...

<a href='https://ko-fi.com/Q5Q3BKI5S' target='_blank'><img height='36' style='border:0px;height:36px;' src='https://cdn.ko-fi.com/cdn/kofi2.png?v=3' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a>

## Audio Files

### Generic Info
There are 2 seperate set of sound files. Audio Files starting with 'D_' and files starting with 'F_'. 
The 'D_' files are the audio files for desktop mode, the 'F_' files for fullscreen mode.
If you don't want to hear a certain file you can just delete the wav file of the event you don't want to hear.
You can also change the files with your own files. Be sure to use the 'Open Audio Files Folder' menu for doing so.
It will make sure loaded audio files get closed so you can overwrite them. Make sure playnite does not play any audio	anymore after opening the folder or it's possible you can't overwrite that specific file. With my testings it did seem you could still first erase the files. After changing the audio files use the 'Reload Audio Files' menu to clear any loaded files and use your new files, or just restart Playnite. Do NOT use a long audio file for ApplicationStopped as Playnite will not quit until that audio file has finished playing. The same applies for switching between desktop and fullscreen mode.

### Audio files that can be used
They are located in the sounds folder of the extension, you can open that folder using the main playnite menu -> Extensions -> Playnite sounds -> Open Audio Files Folder

| Event         | Desktop       | Fullscreen |
| ------------- |---------------|-------|
| Playnite Startup | D_ApplicationStarted.wav | F_ApplicationStarted.wav |
| Playnite Exit     | D_ApplicationStopped.wav | F_ApplicationStopped.wav |
| Game Installed | D_GameInstalled.wav | F_GameInstalled.wav |
| Game UnInstalled | D_GameUninstalled.wav | F_GameUninstalled.wav |
| Game Selected | D_GameSelected.wav |  F_GameSelected.wav |
| Game Starting | D_GameStarting.wav | F_GameStarting.wav |
| Game Started | D_GameStarted.wav | F_GameStarted.wav |
| Game Stopped | D_GameStopped.wav | F_GameStopped.wav |
| Game LibraryUpdated | D_LibraryUpdated.wav | F_LibraryUpdated.wav |

### Create your own Audio files
A very simple and free tool to create (game) sounds is SFXR, you can use it to create certain blip and blop sounds and perhaps use it to create your own sound files to be used with Playnite Sound extension. If you want to record your own sounds or edit existing sounds you could use audacity

SFXR: https://www.drpetter.se/project_sfxr.html

Audacity: https://www.audacityteam.org/

### Example video
[![Playnite Sound Example Video](http://img.youtube.com/vi/zXzSdLrOmtw/0.jpg)](http://www.youtube.com/watch?v=zXzSdLrOmtw "Playnite Sound Example Video")

### Playnite Sound V2.0 Release Video
[![Playnite Sound V2.0 Release Video](http://img.youtube.com/vi/iTZ9JbswN3M/0.jpg)](https://youtu.be/iTZ9JbswN3M "Playnite Sound V2.0 Release Video")

### Playnite Sound V3.0 Release Video
[![Playnite Sound V2.0 Release Video](http://img.youtube.com/vi/NL1c7puTPz8/0.jpg)](https://youtu.be/NL1c7puTPz8 "Playnite Sound V3.0 Release Video")

### Playnite Sound V4.0 Release
* Playnite 9 Support
* First Platform will be used for games having multiple platforms set !!!
* Be aware platform "PC" changed to "PC (Windows)" in playnite 9 so change your music folder for that platform accordingly in extension data folder.
* Updated Localizations


## Translation
The project is translatable on [Crowdin](https://crowdin.com/project/playnite-game-speak)

Thanks to the following people who have contributed with translations:
* Spanish: Darklinpower
* French: M0ylik
* Polish: Renia
* Italian: Federico Pezzuolo (3XistencE-STUDIO), StarFang208
* German: kristheb
* Hungarian: myedition8
* Porutgese, Brazillian: atemporal_ (Atemporal), JCraftPlay 
* Ukrainian: SmithMD24
* Norwegian: MeatBoy 
* Czech: SilverRoll (silveroll)
* Korean: Min-Ki Jeong
* Chinese Simplified: ATNewHope
* Arabic: X4Lo

## Credits
* Used Icon made by [Freepik](http://www.freepik.com/)
* Original Localization file loader by [Lacro59](https://github.com/Lacro59)
* Sound Manager by [dfirsht](https://github.com/dfirsht)
