# P3Distrib
[P3Distrib](https://github.com/clempo2/P3Distrib) is a pair of utilities to create and apply patches to distribute P3 Unity projects.

A P3 Unity project is an application written in [Unity](https://unity.com/) using the [Multimorphic](https://www.multimorphic.com/) [P3 SDK](https://www.multimorphic.com/support/projects/customer-support/wiki/3rd-Party_Development_Kit).

The License for the Multimorphic P3 SDK is proprietary making it difficult
to distribute open source projects built on top of P3SampleApp.
P3Distrib solves this problem by distributing only the changes between
the modified project and P3SampleApp.

Like any typical diff program, P3Distrib does not store identical content. It also goes further in the following ways:
- The comparison is done at the character level
- It does not store any surrounding context
- It does not store a pre-image of deleted text

Applying the patch to recreate the modified project requires access to an unmodified copy of the source project.

# Installation

P3Distrib is distributed in source form.

- clone the P3Distrib repository locally
- open the P3Distrib.sln solution in Visual Studio
- Right-click the solution and select Rebuild Solution
- This will produce P3Diff.exe and P3Patch.exe

# Creating a Patch

To create a patch, run the command:  
    ```
    P3Diff.exe <sourcePath> <destinationPath>
    ```  
The patch is saved at the destinationPath with the extension .p3patch appended.

For example:  
    ```
    P3Diff.exe c:\P3\P3_SDK_V0.8\P3SampleApp C:\P3\P3EmptyGame  
    ```  
saves the patch in C:\P3\P3EmptyGame.p3patch

# Applying a Patch

To apply a patch and recreate the modified P3 project, run the command:  
    ```
    P3Patch.exe <sourcePath> <p3patchPath>  
    ```  
The output project is under the directory p3patchPath without the extension.

For example:  
    ```
    P3Patch.exe c:\P3\P3_SDK_V0.8\P3SampleApp c:\P3\P3EmptyGame.p3patch  
    ```  
saves a copy of the project under c:\P3\P3EmptyGame

## Support

Please submit a [GitHub issue](https://github.com/clempo2/P3Distrib/issues) if you find a problem.

You can discuss P3Distrib and other P3 Development topics on the [P3 Community Discord Server](https://discord.gg/GuKGcaDkjd) in the dev-forum channel under the 3rd Party Development section.

## License

Copyright (c) 2023 Clement Pellerin  
P3Distrib is licensed under the Apache 2.0 license.

[Diff Match and Patch](https://github.com/google/diff-match-patch)  
Copyright 2018 The diff-match-patch Authors.  
Licensed under the Apache 2.0 license

Beware the output of P3Patch is still governed by the licensing terms of the original source project.

