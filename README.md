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
Blazemoji is a modern web application designed specifically for emojicode. It's about time we start building a great editor for a great language.

Features include:
- Compile and run emojicode right in the browser!
- Light / Dark theme built in
- Emojicode quick reference toolbox
- Code editor keybindings for common emojis like `shift`+`"` turns into `🔤`
- Library of sample scripts with support for saving scripts in local storage
- More coming!

## Installation & Build
Clone the repo and run 

```csharp
dotnet buildd
```

### Prerquisites
- dotnet 7 installed
- C# installed
- Visual Studio installed, recommended 2022+
- [WSL](https://learn.microsoft.com/en-us/windows/wsl/install) installed, windows only
- Ubuntu 18.04 or later if WSL
- C++ compiler and linker, such as `clang++` or `g++` 

## Setup & Configuration
Blazemoji depends on being launched in a host environment that can execute the `emojicodec` compiler. Blazor can be launched in a WSL environment targetting Linux from Windows, so this is what we'll need to do in order to compile and run the emojicode correctly. To run everything correctly in WSL, you will have to go through the WSL setup and install steps.

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
