# Inherited legacy part assets

These upstream notes are retained for asset provenance and possible backports,
not as a kOSIM support claim. This fork validates Windows KSP 1.12.5 under Proton
only; do not mix legacy meshes into a normal fork package.

### Original upstream notes: KSP older than 1.8

...These files had to be re-generated for KSP 1.8, by running PartTools again, because KSP 1.8 was upgraded to Unity 2019.2.2f1, and the shaders in the old .mu files didn't work quite right with the newer Unity.

...Therefore, these .mu files might not work on versions of KSP older than 1.8.

...To see what they used to look like before KSP 1.8, see this folder:

``Resources/GameData_Legacy_pre_KSP1.8/kOS/Parts/``

If you ever make a backport of kOS for an older version of KSP, you will need
to copy these older files on top of the ones in
`Resources/GameData/kOS/Parts/`.

