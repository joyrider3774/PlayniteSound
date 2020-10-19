# Playnite Sounds Extension
Playnite Sounds is an extension to play audio files during Playnite events. 
It can only play WAV audio files and nothing else.

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
