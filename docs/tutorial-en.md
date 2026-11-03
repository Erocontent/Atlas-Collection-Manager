# Atlas Collection Manager · Getting started

Atlas is an open-source Windows manager for standalone mod collections that cannot be published as Nexus Collections or installed through Mod Organizer 2 (MO2). The goal is to give these collections a common launcher while retaining game-specific installation rules. A collection can still reference individual files hosted on Nexus Mods.

The current build supports **Dead or Alive 5: Last Round** and **Resident Evil 4 Remake**. Shadow of the Tomb Raider, Rise of the Tomb Raider, Dead or Alive 6, and other Resident Evil and Capcom games illustrate the workflows Atlas is designed for. Those games are outside the current supported list.

## Set up your game

1. Open **Your Library** and choose a game.
2. Use **Game folder** or **Select .exe** to point Atlas to the installation. For DOA5, choose the folder containing **game.exe**.
3. To download Nexus files, connect your account under **Settings → Nexus Mods**.

## Import and activate a collection

1. Open **Collections → Import collection** and select the supplied **.atlascollection** file.
2. Select the mods you want and click **Resume Collection**. Follow any download prompts on Nexus Mods. To link an archive you already downloaded, use **Choose downloaded archive**.
3. Close the game, click **Activate**, review the mods Atlas will enable, disable, or keep, and confirm. Downloaded mods appear in **Your Library** before activation. Activation applies them to the game folder.
4. Use **Deactivate** to remove the active collection setup. When deleting a collection, you can also choose to remove its exclusive mods.

## Manage individual mods

Use **Import archive** to add supported ZIP, RAR, or 7z packages. Enable or disable mods from the game library. Use **Download Covers** to fetch artwork for linked Nexus mods or **Covers → Choose cover** to use a local image. Select **Play** to launch the game.

## Use AutoLink and ReShade in DOA5

Activate AutoLink and your skins. With the game closed, open **ReShade → Install ReShade**. If Atlas identifies the AutoLink DLL it manages, confirm the compatibility prompt. Select **DirectX 9** in the official installer. Back in Atlas, select your preset under **Default preset** and click **Set as default**.

## Create a collection

Open the separate **Atlas Collection Editor**, choose the game, and create or open a collection. Use **Add mod** to enter its name, source URL, Nexus File ID if applicable, and installation type. For a DOA5 AutoLink skin, choose its character when needed. Use **Export collection** to save the **.atlascollection** file for players to import.
