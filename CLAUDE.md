# StoryCAD Legacy STBX Conversion Tool

The facts about this repo for an AI session. The development process is not in this file.

## What it is

A WinUI 3 tool that converts legacy STBX files. The user picks a folder, and the tool finds the legacy XML outline files in it and converts them.

## Parts

The solution is `LegacySTBXConverter.sln`. It has one project, `LegacySTBXConverter.csproj`, at the top of the repo. The project targets `net8.0-windows10.0.22621.0` and uses the StoryCADLib NuGet package, version 3.2.1.

The repo has no tests. It states no build command.
