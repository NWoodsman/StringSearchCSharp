This app is a dotnet console app.

The usage requires one argument: a directory to run the app.

The app should find all dotnet strings in nested .cs files, and load them into memory.

The app should then provide an interaction loop with a modal menu. The menu has two pop depths described next.

Escape pops the modal depth and puts the mode into waiting mode 0.

Number key 1 puts the mode into scroll mode. Scroll mode uses page up and page down keys to move to the next found string. It should write a line indicating source file name and line position. Then write a snippet of the found string to the console so the user sees context of the string in relation to the source file it came from. The snippet should include lines above and below the found string. In this mode 1, page up and page down allow for scrolling the snippet up and down inside the source file. The text should not scroll the terminal, only replace the visible code snippet in the current terminal extents. The snippet should be styled with syntax highlighting. We could style ourselves or use csharp-ls as a dependency to syntax highlight.

Number key 2 puts the mode into edit mode. 

Escape exits the mode and returns to mode 0.

