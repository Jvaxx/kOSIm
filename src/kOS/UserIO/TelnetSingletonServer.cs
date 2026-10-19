using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using kOS.Module;
using kOS.Safe.Screen;
using kOS.Safe.Utilities;
using UnityEngine;
using kOS.Safe.UserIO;
using Object = UnityEngine.Object;

namespace kOS.UserIO
{
    /// <summary>
    /// Spawned by TelnetMainServer, this handles the connection to just one
    /// single telnet client, leaving the main server free to go back to listening
    /// to other new connecting clients.
    /// </summary>
    /// <remarks>
    /// Threading: the connection's reader and writer threads (see <see cref="TelnetConnectionWorker"/>)
    /// only parse input, fill the queues and write to the socket. Anything touching KSP or Unity,
    /// including ending the session, happens on the game thread: workers request a stop and
    /// <see cref="TelnetMainServer"/> calls <see cref="MainThreadUpdate"/> every frame to act on it.
    /// <br/>
    /// Both queues are bounded (see <see cref="TelnetInputQueue"/> and <see cref="TelnetOutputQueue"/>):
    /// typed text beyond the input backlog is discarded with a notice to the client, interrupts
    /// and resizes are never held behind typed text, and output a stalled client could not take
    /// is replaced by a full repaint.
    /// </remarks>
    public class TelnetSingletonServer : ITelnetConnectionHandler, ITelnetInputSink, ITerminalViewer
    {
        private const double HUNG_CHECK_INTERVAL = 10; // Seconds that a keep alive must have failed to give a response before assuming telnet client is dead.
        private const int WORKER_JOIN_TIMEOUT_MS = 1000; // How long StopListening waits for the worker threads.
        private const int GOODBYE_LOCK_TIMEOUT_MS = 250; // Skip the goodbye message rather than wait on a stalled client.

        // ReSharper disable SuggestUseVarKeywordEvident
        
        // I want to communicate the difference between cases where the fact that the
        // value starts at zero is irrelevant to the algorithm from cases where it's
        // actually part of the algorithm:
        // ReSharper disable RedundantDefaultFieldInitializer
        
        // These exist just to track the state of whether or not the telnet client
        // got told about a change to these statuses yet or not:
        public bool ReverseScreen {get; set;}
        public bool VisualBeep {get; set;}
        
        private readonly TcpClient client;
        
        private readonly TelnetMainServer whoLaunchedMe;
        
        /// <summary>
        /// Owns the socket and the reader/writer threads.
        /// </summary>
        private readonly TelnetConnectionWorker connection;

        private readonly TelnetInputDispatcher inputDispatcher = new TelnetInputDispatcher();
        
        /// <summary>
        /// The keys typed by the telnet client that other parts of kOS read. Thread-safe.
        /// </summary>
        private readonly TelnetInputQueue inQueue = new TelnetInputQueue();

        /// <summary>
        /// Decodes the client's typed text into inQueue, keeping key sequences split across reads whole.
        /// </summary>
        private readonly TelnetInputPipeline inputPipeline;

        /// <summary>
        /// The output that other parts of kOS write to the telnet client. Thread-safe.
        /// </summary>
        private readonly TelnetOutputQueue outQueue = new TelnetOutputQueue();

        private readonly Stopwatch clock = Stopwatch.StartNew(); // monotonic time for the input sequence timeout
        private readonly object overflowLogAccess = new object(); // output can be written from the game and reader threads
        private DateTime nextOutputOverflowLog = DateTime.MinValue;
        
        public kOSProcessor ConnectedProcessor { get; private set; }
        
        private volatile TelnetWelcomeMenu welcomeMenu; // read by the reader thread to flag resizes
        
        private volatile TerminalUnicodeMapper terminalMapper;
        
        /// <summary>
        /// What order was I launched in?  Was I the 1st TelnetSingletonServer? The 2nd?  The 423rd?
        /// </summary>
        private int MySpawnOrder { get; set; }
        
        private volatile bool isLineAtATime;

        // Written by the reader thread, read by the game thread.
        private volatile int clientWidth;
        private volatile int clientHeight;
        public int ClientWidth { get { return clientWidth; } private set { clientWidth = value; } }
        public int ClientHeight { get { return clientHeight; } private set { clientHeight = value; } }

        private string termTypeBackingField; // This is deliberately NOT named as just a lower-case version of ClientTerminalType,
                                             // so as to prevent accidentally typing this name instead of the property.  It's essential
                                             // that all the access even inside this class itself, be done via the property to
                                             // force it to keep the terminalMapper updated to match.
                                             
        private readonly object keepAliveAccess = new object(); // because the timestamps can be seen by both in and out threads.

        private bool gotSomeRecentTraffic = true; // start off assuming it's alive.
        private DateTime keepAliveSendTimeStamp;
        
        private bool alreadyDisconnecting = false;

        private bool stopped; // game thread only

        public string ClientTerminalType
        {
            get{ return termTypeBackingField;}
            private set
            {
                termTypeBackingField = value; 
                terminalMapper = TerminalUnicodeMapper.TerminalMapperFactory(value);
            }
        }

        // Special telnet protocol bytes with magic meaning, taken from Internet RFC's:
        // Many of these will go unused at first, but it's important to get them down for future
        // embetterment.
        //
        // For documentation on how these telnet controls work, and how they're expected
        // to be passed back and forth, do an Internet search based on the RFC number of the
        // constant (i.e. to see how the byte code RFC854_IAC is used, go look up "RFC 854" on
        // the Internet and look at what it says about a byte code called "IAC".)

        private const byte RFC854_SE       = 240; //  End of subnegotiation parameters.
        private const byte RFC854_NOP      = 241; //  No operation.
        private const byte RFC854_DATAMARK = 242; //  The data stream portion of a Synch.
                                                  // This should always be accompanied
                                                  // by a TCP Urgent notification.
        private const byte RFC854_BREAK    = 243; //  NVT character BRK.
        private const byte RFC854_IP       = 244; //  The function IP.
        private const byte RFC854_AO       = 245; //  The function AO.
        private const byte RFC854_AYT      = 246; //  The function AYT. ("Are you There"). Used to check for keepalive timings.
        private const byte RFC854_EC       = 247; //  The function EC.
        private const byte RFC854_EL       = 248; // The function EL.
        private const byte RFC864_GA       = 249; //  The GA signal.
        private const byte RFC854_SB       = 250; //  Indicates that what follows is
                                                  // subnegotiation of the indicated
                                                  // option.
        private const byte RFC854_WILL     = 251; //  Indicates the desire to begin
                                                  // performing, or confirmation that
                                                  // you are now performing, the
                                                  // indicated option.
        private const byte RFC854_WONT     = 252; //   Indicates the refusal to perform,
                                                  // or continue performing, the
                                                  // indicated option.
        private const byte RFC854_DO       = 253; // Indicates the request that the
                                                  // other party perform, or
                                                  // confirmation that you are expecting
                                                  // the other party to perform, the
                                                  // indicated option.
        private const byte RFC854_DONT     = 254; // Indicates the demand that the
                                                  // other party stop performing,
                                                  // or confirmation that you are no
                                                  // longer expecting the other party
                                                  // to perform, the indicated option.
        private const byte RFC854_IAC      = 255; // Interpret as Command - the escape char to start all the above things.
        
        private const byte RFC857_ECHO     = 1;
        
        private const byte RFC858_SUPPRESS_GOAHEAD = 3;
        
        private const byte RFC1184_LINEMODE = 34;
        private const byte RFC1184_MODE     = 1;
        private const byte RFC1184_EDIT     = 1;
        private const byte RFC1184_TRAPSIG  = 2;
        private const byte RFC1184_MODE_ACK = 4;
        private const byte RFC1184_SOFT_TAB = 8;
        private const byte RFC1184_LIT_ECHO = 16;
        
        private const byte RFC1091_TERMTYPE = 24;
        private const byte RFC1091_IS       = 0;
        private const byte RFC1091_SEND     = 1;

        private const byte RFC1073_NAWS     = 31;
        
        public TelnetSingletonServer(TelnetMainServer mainServer, TcpClient client, int spawnOrder)
        {
            whoLaunchedMe = mainServer;
            MySpawnOrder = spawnOrder;
            this.client = client;
            lock (keepAliveAccess)
                gotSomeRecentTraffic = true;
            inputPipeline = new TelnetInputPipeline(inQueue, "INITIAL_UNSET", TelnetInputPipeline.DefaultSequenceTimeout);
            ClientWidth = 80; // common default for a lot of terminal progs.  Will allow resize via RFC1073.
            ClientHeight = 24; // common default for a lot of terminal progs.  Will allow resize via RFC1073.
            ClientTerminalType = "INITIAL_UNSET"; // will be set by telnet client as described by RFC 1091
            connection = new TelnetConnectionWorker(client, this, "kOS telnet " + spawnOrder);
            SpawnWelcomeMenu();
        }

        // There is deliberately no finalizer: Unity objects (the welcome menu) can only be
        // destroyed from the game thread, which StopListening() does.

        /// <summary>
        /// Connect this TelnetSingletonServer to a CPU to start acting as its terminal.
        /// </summary>
        /// <param name="processor">The kOSProcessor PartModule to attach to</param>
        public void ConnectToProcessor(kOSProcessor processor)
        {
            ConnectedProcessor = processor;
            if (welcomeMenu != null)
                welcomeMenu.Detach(); // so it stops consuming input meant for the CPU this frame
            // Tell the GUI window that I am one of its telnets now (even when closed, it still does the heavy lifting).
            // It resizes the terminal to match the telnet window, then sends the title and a full paint:
            ConnectedProcessor.GetWindow().AttachTelnet(this);
        }

        void ITerminalViewer.Detach()
        {
            DisconnectFromProcessor();
        }

        public void DisconnectFromProcessor()
        {
            if (ConnectedProcessor != null && !alreadyDisconnecting)
            {
                alreadyDisconnecting = true; // telnet tells window to disconnect, window tells telnet to disconnect.  This stops an infinite recursion.
                string detachMessage = "Detaching from " + ConnectedProcessor.GetWindow().TitleText;
                SendTextRaw( (char)UnicodeCommand.TITLEBEGIN + detachMessage + (char)UnicodeCommand.TITLEEND );
                SendTextRaw("\r\n{" + detachMessage + "}\r\n");
                ConnectedProcessor.GetWindow().DetachTelnet(this);

                // Very important, else CPU parts that go away (from explosion, load distance, scene switch ,etc) refuse to be orphaned and won't garbage collect.
                // That can cause subsequent reconnections from the TelnetWelcomeMenu to end up connecting you to the now dead CPU, with confusing results:
                ConnectedProcessor = null;
                alreadyDisconnecting = false;

                // Disconnecting the CPU from this, so connect the welcome menu instead
                if (welcomeMenu != null)
                {
                    welcomeMenu.Attach(this);
                }
            }
        }

        /// <summary>
        /// Get the next available char from the client.
        /// Can throw exception if there is no such char.  Check ahead of time to see if
        /// there's at least one character available using InputWaiting().
        /// </summary>
        /// <returns>one character read</returns>
        public char ReadChar()
        {
            char ch;
            if (!inQueue.TryDequeue(true, out ch))
                throw new InvalidOperationException("No telnet input is waiting.");
            return ch;
        }

        /// <summary>
        /// Get the next key, if there is one. Interrupts, resizes and repaint requests come
        /// before typed text; typed text only if <paramref name="includeText"/> is true, so
        /// a reader that cannot take more text still gets the control keys.
        /// </summary>
        public bool TryReadChar(bool includeText, out char ch)
        {
            return inQueue.TryDequeue(includeText, out ch);
        }
        
        /// <summary>
        /// Get a string of all available characters from the client.
        /// If no such chars are available it will not throw an exception.  Instead it just returns a zero length string.
        /// </summary>
        /// <returns>All currently available input characters returned in one string.</returns>
        public string ReadAll()
        {
            return inQueue.DequeueAll();
        }
        
        /// <summary>
        /// Determine if the input queue has pending chars to read.  If it isn't, then attempts to call ReadOneChar will throw an exception.
        /// </summary>
        /// <returns>
        /// true if input is currently Queued
        /// </returns>
        public bool InputWaiting()
        {
            return inQueue.HasInput(true);
        }

        /// <summary>Typed characters waiting to be read.</summary>
        public int InputBacklog { get { return inQueue.TextCount; } }

        /// <summary>Typed characters discarded because the input backlog was full.</summary>
        public long DiscardedInputCount { get { return inQueue.DroppedCount; } }

        /// <summary>Characters waiting to be sent to the client.</summary>
        public int OutputBacklog { get { return outQueue.Count; } }

        /// <summary>How often unsent output was replaced by a full repaint because the client fell behind.</summary>
        public int OutputOverflowCount { get { return outQueue.OverflowCount; } }
        
        /// <summary>
        /// Write a character out to the telnet client.  You can pretend the telnet client is a
        /// pseudo-terminal that understands the command codes in UnicodeCommand.
        /// <br/>
        /// This TelnetSingletonServer will run what you give it through a command code
        /// mapper for whichever terminal type is actually attached to the telnet client.
        /// <br/>
        /// Remember that when sending strings to this method, end-of-lines need
        /// to be expressed as "\r\n", because you are writing using the raw Internet
        /// ASCII standard.
        /// </summary>
        /// <param name="ch">character to write</param>
        public void Write(char ch)
        {
            if (outQueue.Write(ch))
                OutputOverflowed();
        }

        /// <summary>
        /// Write a string out to the telnet client.  You can pretend the telnet client is a
        /// pseudo-terminal that understands the command codes in UnicodeCommand.
        /// <br/>
        /// This TelnetSingletonServer will run what you give it through a command code
        /// mapper for whichever terminal type is actually attached to the telnet client.
        /// <br/>
        /// Remember that when sending strings to this method, end-of-lines need
        /// to be expressed as "\r\n", because you are writing using the raw Internet
        /// ASCII standard.
        /// </summary>
        /// <param name="str">string to write</param>
        public void Write(string str)
        {
            if (outQueue.Write(str))
                OutputOverflowed();
        }

        /// <summary>
        /// The client fell so far behind that unsent output was dropped: have whoever is
        /// drawing to it (the terminal window or the welcome menu) repaint everything.
        /// </summary>
        private void OutputOverflowed()
        {
            inQueue.RequestRepaint();
            TelnetWelcomeMenu menu = welcomeMenu;
            if (menu != null)
                menu.NotifyResize(); // only sets a flag; makes the menu reprint itself

            // Logging is rate-limited: a stalled client overflows repeatedly until it times out.
            DateTime now = DateTime.Now;
            lock (overflowLogAccess)
            {
                if (now < nextOutputOverflowLog)
                    return;
                nextOutputOverflowLog = now + TimeSpan.FromSeconds(10);
            }
            SafeHouse.Logger.Log(string.Format("kOS telnet server: connection {0} is not keeping up; dropped unsent output " +
                "and repainting ({1} times, {2} characters so far).", MySpawnOrder, outQueue.OverflowCount, outQueue.DroppedCount));
        }
        
        /// <summary>
        /// Bypasses the Queues and just sends text directly to the socket stream.
        /// </summary>
        private void SendTextRaw(char[] buff)
        {
            SendTextRaw(new string(buff));
        }

        /// <summary>
        /// Bypasses the Queues and just sends text directly to the socket stream.
        /// </summary>
        private void SendTextRaw(string str)
        {
            byte[] outBuff = Encoding.UTF8.GetBytes(str);
            SendTextRaw(outBuff);
        }

        /// <summary>
        /// Bypasses the Queues and just sends text directly to the socket stream.
        /// A dead or stalled client makes this give up (the write times out) and requests
        /// a stop instead of throwing.
        /// </summary>
        private void SendTextRaw(byte[] buff)
        {
            const bool VERBOSE_DEBUG_SEND = false; // enable to print a verbose dump of every char going to the client.
            connection.Send(buff);

#pragma warning disable CS0162
            if (VERBOSE_DEBUG_SEND) // compiler warning - this code block is hardcoded to be unreachable.  But that's deliberate.
            {
                StringBuilder logMessage = new StringBuilder();
                logMessage.Append("kOS Telnet server:  Just wrote the following buffer chunk out to the client:");
                for (int i=0; i<buff.Length; ++i)
                {
                    // Not using SuperVerbose because it's flagged by verboseDebugSend and maybe we want to ask users
                    // to be able to send us logs when they issue bug reports:
                    logMessage.Append("Send buff["+i+"] = (int)"+ (int)buff[i] + " = '" + (char)buff[i] + "'\n");
                }
                SafeHouse.Logger.Log(logMessage.ToString());
            }
#pragma warning restore CS0162
        }

        /// <summary>
        /// End this telnet session. Must be called on the game thread; safe to call more than once.
        /// </summary>
        public void StopListening()
        {
            if (stopped)
                return;
            stopped = true;

            DisconnectFromProcessor();
            if (!connection.IsWriteDead && !connection.IsShutdown)
            {
                // Bypass the output queue, whose thread is about to stop. Don't wait on a client
                // that has stopped reading; the goodbye is a courtesy.
                connection.TrySend(Encoding.UTF8.GetBytes("\r\nDisconnecting from the kOS Terminal Server.\r\n"), GOODBYE_LOCK_TIMEOUT_MS);
                connection.TrySend(Encoding.UTF8.GetBytes(terminalMapper.OutputConvert( (char)UnicodeCommand.TITLEBEGIN + "Thank you for using kOS terminal server" + (char)UnicodeCommand.TITLEEND )), GOODBYE_LOCK_TIMEOUT_MS);
            }
            whoLaunchedMe.SingletonStopped(this);

            if (!connection.Shutdown(TimeSpan.FromMilliseconds(WORKER_JOIN_TIMEOUT_MS)))
                SafeHouse.Logger.LogError(string.Format("kOS telnet server: connection {0} threads did not exit within {1} ms; leaving them as background threads.",
                    MySpawnOrder, WORKER_JOIN_TIMEOUT_MS));

            // Get rid of the welcome menu too, which has a reference to this server and should prevent GC
            if (welcomeMenu != null)
            {
                welcomeMenu.Detach();
                Object.Destroy(welcomeMenu.gameObject);
                welcomeMenu = null;
            }
        }

        public void StartListening()
        {
            lock (keepAliveAccess)
                gotSomeRecentTraffic = true; // Just in case it's been a long time between construction and StartListening.
            connection.Start();
            LineAtATime(false); 
            AllowTerminalResize(true);
            AllowTerminalTypeInfo(true);

            SendTextRaw("Connected to the kOS Terminal Server.\r\n");
        }

        /// <summary>
        /// Game-thread housekeeping, called every frame by <see cref="TelnetMainServer"/>:
        /// ends the session when a worker asked for it, and keeps the welcome menu attached
        /// whenever no CPU is.
        /// </summary>
        internal void MainThreadUpdate()
        {
            if (stopped)
                return;

            if (connection.StopRequested)
            {
                SafeHouse.Logger.SuperVerbose(string.Format("kOS telnet server: ending connection {0} ({1}).", MySpawnOrder, connection.StopReason));
                StopListening();
                return;
            }

            // A key sequence left unfinished for a while was really separate keys (e.g. Escape):
            inputPipeline.FlushIfIdle(clock.Elapsed);
            ReportInputOverflow();

            if (welcomeMenu == null)
                return;
            if (! welcomeMenu.IsAttached)
            {
                if (ConnectedProcessor == null)
                    welcomeMenu.Attach(this);
            }
            else if (ConnectedProcessor != null) // welcome menu is attached but we now have a processor picked, so detach it.
            {
                welcomeMenu.Detach();
            }
        }
        
        /// <summary>
        /// Tell the telnet client that we'd like to operate in line-at-a-time mode, or
        /// conversely, in char-at-a-time mode.  As per RFC 857.
        /// </summary>
        /// <param name="modeOn">true = line-at-a-time, false = char-at-a-time</param>
        public void LineAtATime(bool modeOn)
        {
            isLineAtATime = modeOn;
            if (modeOn)
            {
                SendTextRaw( new[] {RFC854_IAC, RFC854_WONT, RFC857_ECHO}); // we will not be echoing the client input back to it.
                SendTextRaw( new[] {RFC854_IAC, RFC854_DO, RFC857_ECHO}); // so the client should do its own local echoing.
                SendTextRaw( new[] {RFC854_IAC, RFC854_WONT, RFC858_SUPPRESS_GOAHEAD}); // don't send one char at a time, buffer the lines.
            }
            else
            {
                SendTextRaw( new[] {RFC854_IAC, RFC854_WILL, RFC857_ECHO}); // we will echo the client's input back to it.
                SendTextRaw( new[] {RFC854_IAC, RFC854_DONT, RFC857_ECHO}); // so the client shouldn't be doing a local echo (or the user would see text twice).
                SendTextRaw( new[] {RFC854_IAC, RFC854_WILL, RFC858_SUPPRESS_GOAHEAD}); // do send one char at a time without buffering lines.
            }
        }
        
        /// <summary>
        /// Tell the telnet client that we'd like to allow it to resize its window, and if it does
        /// it should tell us what the new size is.  As per RFC 1073.
        /// </summary>
        /// <param name="modeOn">true = client should send resize messages whenever it feels like.  false = it should not send us resize messages.</param>
        public void AllowTerminalResize(bool modeOn)
        {
            SendTextRaw(modeOn
                ? new[] { RFC854_IAC, RFC854_DO, RFC1073_NAWS } // do allow Negotiate About Window Size
                : new[] { RFC854_IAC, RFC854_DONT, RFC1073_NAWS } // don't allow Negotiate About Window Size
                ); 
        }

        /// <summary>
        /// Tell the telnet client that we'd like it to send us ID strings when queried about its terminal model.
        /// </summary>
        /// <param name="modeOn">true = client should send terminal ident information.</param>
        public void AllowTerminalTypeInfo(bool modeOn)
        {
            SendTextRaw(modeOn
                ? new[] { RFC854_IAC, RFC854_DO, RFC1091_TERMTYPE } // do request terminal type info negotiations from client
                : new[] { RFC854_IAC, RFC854_DONT, RFC1091_TERMTYPE } // don't request terminal type info from client  
                );
        }

        /// <summary>
        /// Detect if the client is stuck, using some dummy sends.
        /// The low level TCP keepalive would be another way to do this, but .NET did a little bit TOO much abstraction
        /// of network sockets, and hid the ability to change that setting.   It's stuck at its default value of 2 hours.
        /// To change it from 2 hours would require dropping into the Windows OS-specific libraries and we're trying to
        /// avoid that so KOS can run on Mac and Linux.  (Rant: The ability to choose the TCP/IP keepalive interval
        /// is old bog-standard Internet stuff and in no way is it Windows-specific so there's no reason to restrict
        /// it to the windows-specific part of the API, dammit!  It should be part of .NET because every OS can do it!!)
        /// </summary>
        private bool IsHung()
        {
            bool returnValue;
            lock (keepAliveAccess)
            {
                if (DateTime.Now <= keepAliveSendTimeStamp)
                    return false;

                // By the time it's time to send a second keepalive, we had better have gotten the reply from the previous one:
                returnValue = ! gotSomeRecentTraffic;
                keepAliveSendTimeStamp = DateTime.Now + System.TimeSpan.FromSeconds(HUNG_CHECK_INTERVAL);

                // This will get set to true when we receive any bytes at all from the client,
                // whether they're the answer to our query, or something else like user typing.
                gotSomeRecentTraffic = false;
            }

            // The telnet protocol has no keepalive from server to client message in the protocol, so
            // we'll use the terminal type request as a make-do version of a keepalive.  It should force the
            // telnet client to send some sort of bytes back to us as it answers the terminal type request.
            // (Sent outside the lock so a slow client can't stall the reader thread's traffic bookkeeping.)
            TelnetAskForTerminalType();
            return returnValue;
        }

        /// <summary>
        /// Reader thread: input parsed by the connection, in stream order.
        /// All threads in a KSP mod must be careful NOT to access any KSP or Unity methods
        /// from inside their execution, because KSP and Unity are not threadsafe.
        /// </summary>
        void ITelnetConnectionHandler.OnInput(List<TelnetInputEvent> events, bool moreDataAvailable)
        {
            // As long as some input came recently, no matter what it is, we don't need to bother sending the keepalive:
            lock (keepAliveAccess)
            {
                gotSomeRecentTraffic = true;
                keepAliveSendTimeStamp = DateTime.Now + System.TimeSpan.FromSeconds(HUNG_CHECK_INTERVAL);
            }
            inputDispatcher.Dispatch(events, moreDataAvailable, this);
        }

        /// <summary>
        /// Writer thread, called repeatedly: takes whatever appears on the output queue (that some
        /// other part of kOS put there) and sends it to the client. Also watches for a dead client.
        /// </summary>
        bool ITelnetConnectionHandler.OnWriterTick()
        {
            // Detect if the client closed from its end, or stopped answering keepalives:
            if (!client.Connected)
                connection.RequestStop(TelnetStopReason.ClientClosed);
            else if (IsHung())
                connection.RequestStop(TelnetStopReason.ServerRequested);

            bool containsDie;
            string content = outQueue.TakeAll(out containsDie);
            if (content.Length > 0)
            {
                TerminalUnicodeMapper mapper = terminalMapper;
                if (mapper == null)
                    SendTextRaw(content);
                else
                    SendTextRaw(mapper.OutputConvert(content));
            }

            if (containsDie)
                connection.RequestStop(TelnetStopReason.ServerRequested); // the game thread ends the session
            return content.Length > 0;
        }

        void ITelnetConnectionHandler.OnWorkerFault(Exception exception)
        {
            SafeHouse.Logger.LogError(string.Format("kOS telnet server: connection {0} failed and will be closed: {1}", MySpawnOrder, exception));
        }

        void ITelnetInputSink.EnqueueText(string text)
        {
            inputPipeline.AddText(text, clock.Elapsed);
            ReportInputOverflow();
        }

        void ITelnetInputSink.EnqueueBreak()
        {
            // The dispatcher discards the type-ahead that came with and right after the break,
            // and the queue discards what was still waiting before it.
            inputPipeline.Break();
        }

        /// <summary>
        /// Tell the client when its typing starts being discarded because kOS is not reading
        /// it (the backlog is full), and how much was lost once it resumes. Either thread.
        /// </summary>
        private void ReportInputOverflow()
        {
            if (inQueue.TakeOverflowStarted())
            {
                SafeHouse.Logger.Log(string.Format("kOS telnet server: connection {0} input backlog is full ({1} characters); discarding typed input until it drains.",
                    MySpawnOrder, inQueue.Capacity));
                Write((char)UnicodeCommand.BEEP + "[kOS telnet: input backlog full; discarding typed input until kOS catches up]" + (char)UnicodeCommand.STARTNEXTLINE);
            }
            long dropped;
            if (inQueue.TakeOverflowEnded(out dropped))
            {
                SafeHouse.Logger.Log(string.Format("kOS telnet server: connection {0} input resumed; {1} typed characters were discarded.", MySpawnOrder, dropped));
                Write("[kOS telnet: input resumed; " + dropped + " typed characters were discarded]" + (char)UnicodeCommand.STARTNEXTLINE);
            }
        }

        void ITelnetInputSink.Negotiate(byte verb, byte option)
        {
            string warning;
            byte[] reply = TelnetProtocol.NegotiationReply(verb, option, isLineAtATime, out warning);
            if (reply != null)
                SendTextRaw(reply);
            if (warning != null)
                Write(warning + (char)UnicodeCommand.STARTNEXTLINE);
            SafeHouse.Logger.SuperVerbose(string.Format("telnet protocol negotiation from client: {{{0}}}{{{1}}}", verb, option));
        }

        /// <summary>
        /// The client sent its window size (RFC 1073), i.e. a 350x40 window arrives as
        /// IAC SB NAWS 1 94 0 40 IAC SE. A zero field means "unchanged"; sizes are limited to
        /// what the screen buffer supports.
        /// </summary>
        void ITelnetInputSink.ResizeRequested(int width, int height)
        {
            SafeHouse.Logger.SuperVerbose( "Telnet client just told me its window size is " + width + "x" + height+".");

            int newWidth = TelnetProtocol.NormalizeDimension(width, ClientWidth, ScreenBuffer.MAX_COLUMNS);
            int newHeight = TelnetProtocol.NormalizeDimension(height, ClientHeight, ScreenBuffer.MAX_ROWS);
            if (newWidth == ClientWidth && newHeight == ClientHeight)
                return;
            ClientWidth = newWidth;
            ClientHeight = newHeight;

            // Read before any typed text that is still waiting; only the latest size is kept.
            inQueue.EnqueueResize(newWidth, newHeight);
            TelnetWelcomeMenu menu = welcomeMenu;
            if (menu != null)
                menu.NotifyResize(); // only sets a flag; the menu reprints on the game thread
            if (ConnectedProcessor != null)
                inQueue.RequestRepaint();
        }

        /// <summary>
        /// The client told us its terminal model ident (RFC 1091), i.e. "VT100".
        /// </summary>
        void ITelnetInputSink.TerminalTypeReceived(string terminalType)
        {
            // Avoid reconstructing the terminal mapper every time there's a keepalive heartbeat response:
            if (ClientTerminalType != terminalType)
            {
                inputPipeline.SetTerminalType(terminalType);
                ClientTerminalType = terminalType;
            }
            SafeHouse.Logger.SuperVerbose(string.Format("Telnet client just told us its terminal type is: \"{0}\".", terminalType));
        }

        void ITelnetInputSink.ProtocolNotice(TelnetInputEvent notice)
        {
            if (notice.Kind == TelnetInputEventKind.Malformed)
                SafeHouse.Logger.Log("kOS telnet server: discarded malformed input from client: " + notice.Text);
            else
                SafeHouse.Logger.SuperVerbose("telnet protocol message from client ignored: " + notice.Text);
        }
        
        /// <summary>
        /// When the telnet client connects but is not associated with a particular kOSProcessor at the moment,
        /// this will attach the welcome menu to it instead of having it attached to a kOSProcessor.
        /// <br/>
        /// The purpose of the welcome menu is to let the user pick which kOSProcessor to attach to.
        /// </summary>
        private void SpawnWelcomeMenu()
        {
            var gObj = new GameObject( "TelnetWelcomeMenu_" + MySpawnOrder, typeof(TelnetWelcomeMenu) );
            Object.DontDestroyOnLoad(gObj);
            welcomeMenu = (TelnetWelcomeMenu)gObj.GetComponent(typeof(TelnetWelcomeMenu));
            welcomeMenu.Attach(this);
        }
        

        /// <summary>
        /// Tells the telnet client to send me back a terminal ident string.
        /// </summary>
        private void TelnetAskForTerminalType()
        {
            SendTextRaw(TelnetProtocol.TerminalTypeRequest());
        }
    }
}
