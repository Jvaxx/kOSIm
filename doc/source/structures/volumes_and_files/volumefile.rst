.. _volumefile:

VolumeFile
==========

File name and size information. You can obtain a list of values of type VolumeFile using the :ref:`LIST FILES <list command>` command.

.. structure:: VolumeFile

    .. list-table:: Members
        :header-rows: 1
        :widths: 1 1 4

        * - Suffix
          - Type
          - Description

        * - All suffixes of :struct:`VolumeItem`
          -
          - :struct:`VolumeFile` objects are a type of :struct:`VolumeItem`

        * - :meth:`READALL`
          - :struct:`FileContent`
          - Reads file contents
        * - :meth:`READRAW`
          - :struct:`FileContent`
          - Reads exact bytes on built-in volumes, without text conversion
        * - :meth:`WRITERAW(FileContent)`
          - :struct:`Boolean`
          - Appends exact bytes without adding a newline
        * - :meth:`WRITE(String|FileContent)`
          - :struct:`Boolean`
          - Writes the given string to the file
        * - :meth:`WRITELN(string)`
          - :struct:`Boolean`
          - Writes the given string and a newline to the file
        * - :meth:`CLEAR`
          - None
          - Clears this file


.. method:: VolumeFile:READALL

    :return: :struct:`FileContent`

    Reads the legacy content view. On the archive, this may normalize CRLF to LF
    and decode/re-encode content classified as text. This is not safe for arbitrary
    binary data; use :meth:`VolumeFile:READRAW` for byte-preserving reads.

.. method:: VolumeFile:READRAW

    :return: :struct:`FileContent`

    Reads the complete file without decoding or newline conversion on built-in
    archive/local volumes. ``BINARY`` and ``LENGTH`` describe the actual bytes.
    ``STRING`` still uses UTF-8 replacement decoding and retains a UTF-8 BOM.
    Third-party volume providers must implement raw support for the same guarantee;
    legacy providers fall back to their existing read behavior.

.. method:: VolumeFile:WRITERAW(FileContent)

    :return: :struct:`Boolean`

    Appends the supplied bytes unchanged on built-in volumes. Adds no newline and
    accepts only :struct:`FileContent`, not strings or byte lists. Returns false
    when the existing volume append operation rejects the write (for example,
    insufficient space). This is not atomic replacement. Legacy third-party
    providers fall back to their existing write behavior until they opt in.

.. method:: VolumeFile:WRITE(String|FileContent)

    :return: :struct:`Boolean`

    Appends the given string or a :struct:`FileContent` using legacy text behavior.
    Archive conversion on Windows normalizes LF/CRLF to CRLF without adding extra
    CRs on repeated conversion. Use :meth:`VolumeFile:WRITERAW` to append arbitrary
    bytes unchanged. Returns true if successful (lack of space on the :struct:`Volume`
    can cause a failure).

.. method:: VolumeFile:WRITELN(string)

    :return: :struct:`Boolean`

    Writes the given string followed by a newline to the file. Returns true if successful.

.. method:: VolumeFile:CLEAR

    :return: None

    Clears this file
