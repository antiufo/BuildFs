# BuildFs
A [Dokan](http://dokan-dev.github.io/) file system that keeps track of files read/written by build tools and other commands.

Dependencies between commands are automatically found, and commands are re-executed only if some dependant files have changed since the last execution.

## Example
```csharp
/* omitted initialization, see Main() */

fs.AddProject(@"C:\Physical\Path\Example", "example");
// R:\example will now represent/virtualize the physical folder

fs.RunCached("example", "subdir", "nmake", "component-1");
fs.RunCached("example", "subdir", "nmake", "component-2");
fs.RunCached("example", "subdir", "nmake", "component-3");
```
