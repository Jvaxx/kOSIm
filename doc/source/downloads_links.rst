Downloads and Community Links
=============================

Fork downloads are separate from upstream kOS. The tutorials and community links
below are inherited resources, not endorsements or fork-specific support channels.

.. _obtain:

Obtaining kOS Improved
----------------------

Download packaged builds from `this fork's releases <https://github.com/Jvaxx/kOSIm/releases>`_.
Extract ``GameData/kOS`` into your game's ``GameData`` folder. ModuleManager is
required separately. Back up saves, scripts and configuration before updating;
preserve ``GameData/kOS/Plugins/PluginData/``.

**kOSIM replaces upstream kOS; do not install both.** The compatible assembly/part
names and ``GameData/kOS`` location are intentionally unchanged. Runtime validation
covers Windows KSP 1.12.5.3190 under GE-Proton11-7 on Linux, not other platforms.

The fork's CKAN identifier is ``kOSImproved``. It provides ``kOS`` to add-ons that
depend on kOS and conflicts with upstream kOS. Releases from 1.6.0.103 attach a
``.ckan`` file to install with CKAN; the fork is not yet in the main CKAN index.
Remove a CKAN-installed upstream kOS through CKAN first: overwriting a CKAN-managed
install by hand does not transfer its registry ownership. See the repository's
`CKAN packaging notes <https://github.com/Jvaxx/kOSIm/blob/master/docs/ckan.md>`_.

`Upstream releases <https://github.com/KSP-KOS/KOS/releases>`_, upstream CKAN entries,
SpaceDock and CurseForge distribute **upstream kOS**, not this fork.

.. _changelog:

Change Log
----------

- `Fork changelog <https://github.com/Jvaxx/kOSIm/blob/master/CHANGELOG.md>`_
  includes the fork's changes and clearly labelled inherited upstream history.
- :ref:`changes` retains historical upstream documentation notes.

.. _koscommunity:

Online Community
----------------

These independent upstream communities share general kerboscript ideas and examples.
For fork-specific problems use :ref:`gettinghelp`:

- `/r/kos on reddit <https://www.reddit.com/r/kos>`__
- `On Kerbal Space Program's forum <https://forum.kerbalspaceprogram.com/index.php?/topic/165628-*/>`__

.. _showandtell:

Show and Tell, and Tutorials
----------------------------

    There are a number of people who have created helpful tutorials for kOS, and
    others who've created show and tell videos showing the nifty things they've done with kOS.
    These links were inherited from upstream and may show older versions of the mod.

    Note that there is category overlap as often a showoff video is also a sort of tutorial too.

**Tutorials**

    - `Seth Persigehl's Tutorial playlist <https://www.youtube.com/playlist?list=PLEpK8nolWr1rghS5cOEOL-JQ33h4CgPAP>`__
    - `CheersKevinGames <https://www.youtube.com/watch?v=fNlAME5eU3o&list=PLb6UbFXBdbCrvdXVgY_3jp5swtvW24fYv>`__
      A channel devoted to a career mode game played with kOS from day 1, in which no
      Kerbals themselves are allowed to touch the controls.  All piloting is performed
      by kOS programs.  Also works as a good deep explanation of many topics to new
      users of the mod, and people new to programming in general.

**Show and Tell**

    - `Ozin's amazing drone video <https://www.youtube.com/watch?v=f4SytD3xR0k>`__
    - `Dunbaratu's user scripts playlist <https://www.youtube.com/playlist?list=PLdXwd2JlyAvqamZN_jfcxUc29Q6Kzxqmb>`__
    - `Seth Persihel's SpaceX-style first stage return in FAR <https://www.youtube.com/watch?v=sqqQy8cIVFY>`__
    - `NearlyEnoughDice demonstrating a leg leveller <https://www.youtube.com/watch?v=Hh9RrVTQ6xw>`__
    - `Allmhuran's Mechs with scripted AI <https://www.youtube.com/watch?v=SZoyWcUNAZY>`__
    - `Scripted airplane takeoff, waypoint seek, and landing <https://www.youtube.com/watch?v=HA4DdVGuBxc>`__
      Some of these are very old and out of date, going back as far as kOS version 0.65.  The actual code
      used may have to be tweaked a bit to work with recent kOS releases.
    - `Parasite's first docking script, reddit post <http://www.reddit.com/r/Kos/comments/2w3sq5/first_working_docking_script_info_in_comments/>`__
