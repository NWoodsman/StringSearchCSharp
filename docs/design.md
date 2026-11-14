This app is a dotnet console app.

The usage requires one argument: a directory to run the app.

The app should find all dotnet strings in nested .cs files, and load them into memory. We need to store them as a list, and as a hashset as described below.  We want to ignore files that max the regex /Tests?/. We want to write a file to the directory top level called .searches which will store past search runs. The file usage is described below.

The app should then provide an interaction loop with a modal menu. 

At the top of the working pane, we should draw a metadata section with a position bar with a symbol indicating position in the found results. The bar should end with a XX/XXX count of current position / all strings. As the user moves to each found string, the bar length grows or shrinks. Lets use braille dots with coloring to show how close or end the user is to the end of all found string results. Below the position bar should be other metadata as described below. Below this metadata section is the snippet pane.

Below the snippet pane can be another metadata bar with cursor line position, edit size, UTF type, and whatever else you think is necessary.

The modal menu has two pop depths described next.

Escape pops the modal depth and puts the mode into waiting mode 0.

Number key 1 puts the mode into scroll mode. Scroll mode uses page up and page down keys to move to the next found string. It should write a line indicating source file name, line position to the metadata bar, and update the position bar to the index of the current string. Then write a snippet of the found string to the snippet pane so the user sees context of the string in relation to the source file it came from. The snippet should include lines above and below the found string. In this mode 1, page up and page down allow for scrolling the snippet up and down inside the source file. The text should not scroll the terminal, only replace the visible code snippet in the snippet pane. The snippet should be styled with syntax highlighting. We could style ourselves or use csharp-ls as a dependency to syntax highlight. arrow keys should shift the snippet one line, page keys shift multiple lines (5 or 6 maybe).

Number key 2 puts the mode into edit mode. Edit mode puts a cursor into the current snippet and allows for editing the string and adjacent context. We track the insertions as a patch to the original file. page up and page down, arrow up and arrow down continue to allow shifting the snippet. Ctrl+S asks if we want to commit the changes: enter commits them, escape stops the changes and returns to the modified buffer. Escape pops the buffer back to scroll mode directly if the diff is empty; if the diff has changes, we ask again with "Unsaved changes, escape discards them, enter stays here editing". If the user wants to discard, this second escape press pops scroll mode back to waiting mode zero. Pressing number 1 in this mode has the same behavior as escape asking to quit or commit.

As part of edit mode, when the string's patch is a nonempty diff, and the user commits the change, we remove the old string from a hashset and store the new string in a hash set. We can then gray out the snippet styling to visually show that the edit is done. The hashset entries should get written to the .searches file. When the user launches the app next run, string matches in the searches are ignored. Occasionally the user can delete the searches file to refresh context and reevaluate all strings. The app logic is intended to work on strings that 1. are not in tests, and 2. are not ignored.

