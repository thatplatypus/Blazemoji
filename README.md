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

## Prerequisites
- .NET 10 SDK
- Docker, for anything that compiles or runs Emojicode

The Emojicode compiler bundled in this repo is the x86_64 Linux build of 1.0 beta 2, so it runs in a `linux/amd64` container. On Apple Silicon, Docker Desktop emulates it.

## Run the app

```bash
docker build -f Blazemoji/Dockerfile -t blazemoji .
docker run --rm -p 127.0.0.1:5080:8080 blazemoji
```

Then open http://localhost:5080.

The app compiles and runs whatever code it is given, with no sign-in. The command above publishes it on this machine only. Do not publish the port on other interfaces unless you trust everyone who can reach them.

`dotnet run --project Blazemoji` also starts the app and is fine for working on the UI, but Run Code only works where the bundled compiler can execute: x86_64 Linux with `g++` and `libtinfo5` installed.

## Running Tests

```bash
docker build -f Blazemoji/Dockerfile --target test .
```

This builds the solution and runs every test, including the ones that compile and run the samples with the real compiler. Docker caches the stage, so a repeat run with unchanged sources prints nothing; add `--progress=plain --no-cache-filter test` to run the tests again and see the summary.

```bash
dotnet test --solution Blazemoji.sln
```

runs the same tests on your machine. The compiler tests skip themselves anywhere other than x86_64 Linux. On x86_64 Linux they run, and need `g++` and `libtinfo5` installed.

## Emojicode
See the official [docs](https://www.emojicode.org/docs/) for more information on emojicode. The [language reference](https://www.emojicode.org/docs/reference/) will be very handy for writing emojicode.

## Contributing

Pull requests are welcome. For major changes, please open an issue first
to discuss what you would like to change.

Please make sure to update tests as appropriate.

## License

[MIT](https://choosealicense.com/licenses/mit/)
