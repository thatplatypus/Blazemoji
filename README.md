# Blazemoji
<pre>                                                                                                    
   (    (                                                
 ( )\   )\      )          (       )            (    (   
 )((_) ((_)  ( /(   (     ))\     (       (     )\   )\  
((_)_   _    )(_))  )\   /((_)    )\  '   )\   ((_) ((_) 
 | _ ) | |  ((_)_  ((_) (_))    _((_))   ((_)    !   (_) 
 | _ \ | |  / _` | |_ / / -_)  | '  \() / _ \   | |  | | 
 |___/ |_|  \__,_| /__| \___|  |_|_|_|  \___/  _/ |  |_| 
                                              |__/      
</pre>

```emojicode
🏁 🍇
    😀 🔤A Blazor 🔥 powered emojicode editor🔤❗️
🍉
```

![image](https://github.com/thatplatypus/Blazemoji/assets/29233866/f63a3fc3-5e02-4753-b309-bc80a98d1963)

![image](https://github.com/thatplatypus/Blazemoji/assets/29233866/1ccffb14-344a-4810-a855-cab21c4ffbff)

## Overview
Blazemoji is a modern web application designed specifically for emojicode. It's about time we start building a great editor for a great language. The project is still in early stages so all feedback and feature requests are welcome. 

Features include:
- Compile and run emojicode right in the browser!
- Light / Dark theme built in
- Emojicode quick reference toolbox
- Code editor keybindings for common emojis like `shift`+`"` turns into `🔤`
- Library of sample scripts with support for saving scripts in local storage
- More coming soon
  - Researching syntax highlighting in monaco for ☁️ and 🔤

## Prerquisites
- Visual Studio 2022 17.9 or later
- dotnet 8 installed
- Aspire workload for dotnet 8 (optional, recommended for fast setup)
- C# installed
- Visual Studio installed, recommended 2022+
- [WSL](https://learn.microsoft.com/en-us/windows/wsl/install) installed, windows only
- Ubuntu 18.04 or later if WSL
- C++ compiler and linker, such as `clang++` or `g++`, `libncurses5`
- Docker

## Installation & Build
Clone the repo and run 

```csharp
dotnet build
```
### Unix
Depending on permissions you may need to add `sudo` infront of these commands.

```bash
apt-get update
apt-get install libncurses5 -y
apt-get install g++ -y
```

## Setup & Configuration
Blazemoji depends on being launched in a host environment that can execute the `emojicodec` compiler. Blazor can be launched in a WSL environment targetting Linux from Windows, so this is what we'll need to do in order to compile and run the emojicode correctly. To run everything correctly in WSL, you will have to go through the WSL setup and install steps. For some users, this is not an option so we can use a container instead. Blazemoji has a Dockerfile for the `Blazemoji.Compiler` project that will build to the correct OS architecture with dependencies. To communicate with the container from blazemoji, an instance of `rabbitmq` with the management plugin enabled is required.

To make all of that easier, there is a .Net 8 Aspire project that can orchestrate all of the above. Simply launch the http setting from `Blazemoji.AppHost` and it should build and run the compiler container, the blazemoji web app, as well as pull and run rabbitmq with management enabled. This should be the fastest way to get started across any OS. The aspire workload is requuired for this.

## Running Tests
```csharp
dotnet test
```

## Emojicode
See the official [docs](https://www.emojicode.org/docs/) for more information on emojicode. The [language reference](https://www.emojicode.org/docs/reference/) will be very handy for writing emojicode.

## Contributing

Pull requests are welcome. For major changes, please open an issue first
to discuss what you would like to change.

Please make sure to update tests as appropriate.

## License

[MIT](https://choosealicense.com/licenses/mit/)
