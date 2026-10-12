.. _filecontent:

FileContent
================

Represents file contents. Obtain it using :meth:`VolumeFile:READRAW` for exact
bytes on built-in volumes, or :meth:`VolumeFile:READALL` for the legacy content view.

Internally this class stores a byte array. For a new destination, this copies bytes
without text conversion on built-in volumes::

  SET CONTENTS TO OPEN("filename"):READRAW.
  SET NEWFILE TO CREATE("newfile").
  NEWFILE:WRITERAW(CONTENTS).

``WRITERAW`` appends; it is not atomic replacement. ``COPYPATH`` and ``MOVEPATH``
also use raw operations on built-in volumes. Legacy ``READALL`` can normalize or
change archive bytes before they enter this object; raw writing cannot undo that.

You can parse the contents to read them as a string::

  SET CONTENTS_AS_STRING TO OPEN("filename"):READALL:STRING.
  // do something with a string:
  PRINT CONTENTS_AS_STRING:CONTAINS("test").

Instances of this class can be iterated over. In each iteration step a single line of the file will be read.

.. structure:: FileContent

    .. list-table:: Members
        :header-rows: 1
        :widths: 1 1 4

        * - Suffix
          - Type
          - Description


        * - :attr:`LENGTH`
          - :struct:`Scalar`
          - File length (in bytes)
        * - :attr:`EMPTY`
          - :struct:`Boolean`
          - True if the file is empty
        * - :attr:`TYPE`
          - :struct:`String`
          - Type of the content
        * - :attr:`STRING`
          - :struct:`String`
          - Contents of the file decoded using UTF-8 encoding
        * - :attr:`BINARY`
          - :struct:`List`
          - Contents of the file as a list of bytes
        * - :attr:`ITERATOR`
          - :struct:`Iterator`
          - Iterates over the lines of a file
.. note::

    This type is serializable.


.. attribute:: FileContent:LENGTH

    :type: :struct:`Scalar`
    :access: Get only

    Length of the file.

.. attribute:: FileContent:EMPTY

    :type: :struct:`Boolean`
    :access: Get only

    True if the file is empty

.. attribute:: FileContent:TYPE

    :access: Get only
    :type: :struct:`String`

    Heuristic classification from the first four bytes, not a guarantee that content
    is safely textual. Raw I/O does not use this classification to transform data.
    Can be one of the following:\

    TOOSHORT
        Content too short to establish a type

    ASCII
        A file containing ASCII text, like the result of a LOG command.

    KSM
        A type of file containing KerboMachineLanguage compiled code, that was created from the :ref:`COMPILE command <compiling>`.

    BINARY
        Any other type of file.

.. attribute:: FileContent:STRING

    :access: Get only
    :type: :struct:`String`

    Stored bytes decoded using UTF-8 replacement fallback. Invalid sequences appear
    as U+FFFD in the string without changing the stored bytes. A UTF-8 BOM is retained
    as U+FEFF; other encodings are not automatically detected. Raw reads do not normalize
    CRLF in this string. Legacy archive ``READALL`` may have converted the bytes first.

.. attribute:: FileContent:BINARY

    :access: Get only
    :type: :struct:`List`

    Contents of the file as a list of bytes. Each item in the list is a number between 0 and 255 representing a single byte from the file.

.. attribute:: FileContent:ITERATOR

    :access: Get only
    :type: :struct:`Iterator`

    Iterates over the lines of a file
