<div align="center">

**[简体中文](README.zh-CN.md)** &nbsp;|&nbsp; **English**

</div>

---

# NATO ERA v1.2.6

> Forked from [Cyances/NATO-ERA](https://github.com/Cyances/NATO-ERA). Adapted for GHPC `20261014`.

Special thanks to thebeninator for the coding and 3d modelling advice for this mod.

## Features:

<ul>
	<li>Adds configurable ERA to M1/IP Abrams and M2 Bradley</li>
	<li>Configurable ERA arrangement and performance</li>
	<li>Configurable ERA color (so you can match it to the custom skins you have)</li>
	<li>Optional ERA weight penalty</li>
	<li>Compatible with vanilla Abrams, M1A1 Abrams v1.1.4+, M1A1 Abrams AMP v2.3.3+, 50mm Bradley, M3A2 Bradley and M6A2 ADATS mods</li>
	<li>Only the ARAT-1, BRAT M3 and M5 bricks are modelled, not the custom skirts or anything else to minimize potential performance hit</li>
	<li>Triangular pieces such as the M4 brick is not modelled since triangles are not native to Unity Editor</li>
</ul>

## NOTE!

<ul>
	<li>If you want to apply your own textures to the ERA, you may approach me for the required model files</li>
	<li>ARAT-1 weight is 29.48 KG per tile but total value is rounded to the nearest Ones</li>
	<li>I could not find the weight for BRAT so it is assumed to be the same as ARAT</li>
</ul>

![ARAT-1 Standard Array](docs/Images/Standard%20ARAT-1.png)
![BRAT Standard](docs/Images/Standard%20BRAT.png)
![Flork Spec](docs/Images/Flork%20Spec.png)
![Indigo ARAT](docs/Images/Indigo%20ARAT-1.png)

## Installation:

1.) Install [MelonLoader](https://github.com/LavaGang/MelonLoader/).

2.) Download NATO ERA v#.#.#.tar or .zip from the [release page](https://github.com/WpsxX/NatoERA/releases).

3.) Extract the the files and place them in the manner below:

![Mods Folder](docs/Images/Mods%20Folder.PNG)
![NatoAssetsEra Folder](docs/Images/NatoEraAssets%20Folder%20v2.PNG)

4.) Launch the game.

5.) On first time running this mod, the entries in MelonPreferences.cfg will only appear after launching the game then closing it.

## ERA Performance:

| Type  | Protection vs KE (mm) | Protection vs CE (mm) | Note |
| ------------- | ------------- | ------------- | ------------- |
| ARAT-1(M) | 30  | 450  | M version is half size |
| Improved ARAT-1(M) | 150  | 600  | Can be toggled separately |
| BRAT-M3(M) | 30  | 450  | Front hull and extra arrays |
| BRAT-M5 | 45  | 600  | Side hull |
| Improved BRAT-M3(M) | 150  | 600  | Can be toggled separately |
| Improved BRAT-M5 | 200  | 800  | Can be toggled separately |

## Mod Configuration (in UserData/MelonPreferences.cfg):

| Option  | Default config | Added Weight (KG) | Note |
| ------------- | ------------- | ------------- | ------------- |
| M1 ERA  | true | 1,887 | Hull sides array. Also master option to show ERA or not regardless of config below. |
| M1 Hull Lower Front ERA  | false  | 295 |  |
| M1 Hull Upper Front ERA  | false  | 1,150 |  |
| M1 Extended Hull ERA | false  | 769 |  |
| M1 Turret ERA | false | 678 | Turret sides array. Also master option to show other turret ERA arrays or not regardless of config below. |
| M1 Turret Roof ERA | false | 501 |  |
| M1 Turret Cheek ERA | false | 383 |  |
| M1 Extended Turret ERA | false | 413 |  |
| - | - | - | - |
| M1IP ERA  | true | 1,887 | Hull sides array. Also master option to show ERA or not regardless of config below. |
| M1IP Hull Lower Front ERA  | false  | 295 |  |
| M1IP Hull Upper Front ERA  | false  | 1,150 |  |
| M1IP Extended Hull ERA | false  | 769 |  |
| M1IP Turret ERA | false | 737 | Turret sides array. Also master option to show other turret ERA arrays or not regardless of config below. |
| M1IP Turret Roof ERA | false | 501 |  |
| M1IP Turret Cheek ERA | false | 383 |  |
| M1IP Extended Turret ERA | false | 413 |  |
| - | - | - | - |
| Feather ARAT | true |  | Removes weight penalty |
| - | - | - | - |
| Improve CE Protection (600mm) | false |  |  |
| Improve KE Protection (150mm) | false |  |  |
| ARAT R / ARAT G / ARAT B | 71, 80, 65 |  | RGB values. Army green as default. |
| - | - | - | - |
| M2 ERA | true | 2,211 | Hull sides and front array. Master option to show ERA or not regardless of config below. |
| M2 Extended Hull Side ERA | false | 531 |  |
| M2 Hull Side Lower ERA | true | 825 |  |
| M2 Extended Hull Side Lower ERA | false | 118 |  |
| M2 Hull Lower Front ERA | false | 251 |  |
| M2 Hull Upper ERA | false | 671 |  |
| - | - | - | - |
| Feather BRAT | true |  | Removes weight penalty |
| - | - | - | - |
| Improve BRAT-M3 CE Protection (600mm) | false |  |  |
| Improve BRAT-M3 KE Protection (150mm) | false |  |  |
| Improve BRAT-M5 CE Protection (800mm) | false |  |  |
| Improve BRAT-M5 KE Protection (200mm) | false |  |  |
| BRAT R / BRAT G / BRAT B | 71, 80, 65 |  | RGB values. Army green as default. |

This fork adds the following 3 options on top of the original 34. They exist to handle the new game build and M2Extended compatibility. They come after the original entries, so the original order is untouched, and their defaults reproduce the original behaviour:

| Option  | Default config | Added Weight (KG) | Note |
| ------------- | ------------- | ------------- | ------------- |
| NatoEra Caliber Threshold (mm) | 20 |  | Rounds with a smaller caliber only detonate ERA when their penetration exceeds the penetration threshold below. Hardcoded to 20mm in the original, exposed here so it can be tuned. |
| NatoEra Penetration Threshold (mm) | 10 |  | Penetration needed for a below-threshold-caliber round to detonate ERA. Hardcoded to 10mm in the original. |
| NatoEra M2Extended Overlay | true |  | Set to true to shift BRAT outwards when the M2Extended addon armor is fitted, so it sits outside it. Has no effect when M2Extended is not installed. |

The ERA arrangement for the units can be changed when (re)loading missions without restarting the game. Simply alt tab to edit the .cfg file, save the file then restart the mission or start a new one. However, to see updated ARAT performance or color, the game needs to be restarted.

![Configuration File](docs/Images/NATO%20ERA%20MelonPreferences%20v2.PNG)
