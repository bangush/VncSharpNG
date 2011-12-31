// VncSharp - .NET VNC Client Library
// Copyright (C) 2008 David Humphrey
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Reflection;
using System.Windows.Forms;
using System.ComponentModel;
using System.Drawing.Imaging;

namespace VncSharp
{
	/// <summary>
	/// Event Handler delegate declaration used by events that signal successful connection with the server.
	/// </summary>
	public delegate void ConnectCompleteHandler(object sender, ConnectEventArgs e);
	
	/// <summary>
	/// When connecting to a VNC Host, a password will sometimes be required.  Therefore a password must be obtained from the user.  A default Password dialog box is included and will be used unless users of the control provide their own Authenticate delegate function for the task.  For example, this might pull a password from a configuration file of some type instead of prompting the user.
	/// </summary>
	public delegate string AuthenticateDelegate();

	/// <summary>
	/// SpecialKeys is a list of the various keyboard combinations that overlap with the client-side and make it
	/// difficult to send remotely.  These values are used in conjunction with the SendSpecialKeys method.
	/// </summary>
	public enum SpecialKeys {
		CtrlAltDel,
		AltF4,
		CtrlEsc, 
		Ctrl,
		Alt
	}

	[ToolboxBitmap(typeof(RemoteDesktop), "Resources.vncviewer.ico")]
	/// <summary>
	/// The RemoteDesktop control takes care of all the necessary RFB Protocol and GUI handling, including mouse and keyboard support, as well as requesting and processing screen updates from the remote VNC host.  Most users will choose to use the RemoteDesktop control alone and not use any of the other protocol classes directly.
	/// </summary>
	public class RemoteDesktop : Panel
	{
		[Description("Raised after a successful call to the Connect() method.")]
		/// <summary>
		/// Raised after a successful call to the Connect() method.  Includes information for updating the local display in ConnectEventArgs.
		/// </summary>
		public event ConnectCompleteHandler ConnectComplete;
		
		[Description("Raised when the VNC Host drops the connection.")]
		/// <summary>
		/// Raised when the VNC Host drops the connection.
		/// </summary>
		public event EventHandler	ConnectionLost;

        [Description("Raised when the VNC Host sends text to the client's clipboard.")]
        /// <summary>
        /// Raised when the VNC Host sends text to the client's clipboard. 
        /// </summary>
        public event EventHandler   ClipboardChanged;

		/// <summary>
		/// Points to a Function capable of obtaining a user's password.  By default this means using the PasswordDialog.GetPassword() function; however, users of RemoteDesktop can replace this with any function they like, so long as it matches the delegate type.
		/// </summary>
		public AuthenticateDelegate GetPassword;
		
		Bitmap desktop;						     // Internal representation of remote image.
		Image  designModeDesktop;			     // Used when painting control in VS.NET designer
		VncClient vnc;						     // The Client object handling all protocol-level interaction
		int port = 5900;					     // The port to connect to on remote host (5900 is default)
		bool passwordPending = false;		     // After Connect() is called, a password might be required.
		bool fullScreenRefresh = false;		     // Whether or not to request the entire remote screen be sent.
        VncDesktopTransformPolicy desktopPolicy;
		RuntimeState state = RuntimeState.Disconnected;

		private enum RuntimeState {
			Disconnected,
			Disconnecting,
			Connected,
			Connecting
		}
		
		public RemoteDesktop() : base()
		{
			// Since this control will be updated constantly, and all graphics will be drawn by this class,
			// set the control's painting for best user-drawn performance.
			SetStyle(ControlStyles.AllPaintingInWmPaint | 
					 ControlStyles.UserPaint			|
					 ControlStyles.DoubleBuffer			|
                     ControlStyles.Selectable           |   // BUG FIX (Edward Cooke) -- Adding Control.Select() support
					 ControlStyles.ResizeRedraw			|
					 ControlStyles.Opaque,				
					 true);

			// Show a screenshot of a Windows desktop from the manifest and cache to be used when painting in design mode
			designModeDesktop = Image.FromStream(Assembly.GetAssembly(GetType()).GetManifestResourceStream("VncSharp.Resources.screenshot.png"));
			
            // Use a simple desktop policy for design mode.  This will be replaced in Connect()
            desktopPolicy = new VncDesignModeDesktopPolicy(this);
            AutoScroll = desktopPolicy.AutoScroll;
            AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;

			// Users of the control can choose to use their own Authentication GetPassword() method via the delegate above.  This is a default only.
			GetPassword = new AuthenticateDelegate(PasswordDialog.GetPassword);
		}
		
		[DefaultValue(5900)]
		[Description("The port number used by the VNC Host (typically 5900)")]
		/// <summary>
		/// The port number used by the VNC Host (typically 5900).
		/// </summary>
		public int VncPort {
			get { 
				return port; 
			}
			set { 
				// Ignore attempts to use invalid port numbers
				if (value < 1 | value > 65535) value = 5900;
				port = value;	
			}
		}

		/// <summary>
		/// True if the RemoteDesktop is connected and authenticated (if necessary) with a remote VNC Host; otherwise False.
		/// </summary>
		public bool IsConnected {
			get {
				return state == RuntimeState.Connected;
			}
		}
		
		// This is a hack to get around the issue of DesignMode returning
		// false when the control is being removed from a form at design time.
		// First check to see if the control is in DesignMode, then work up 
		// to also check any parent controls.  DesignMode returns False sometimes
		// when it is really True for the parent. Thanks to Claes Bergefall for the idea.
		protected new bool DesignMode {
			get {
				if (base.DesignMode) {
					return true;
				} else {
					Control parent = Parent;
					
					while (parent != null) {
						if (parent.Site != null && parent.Site.DesignMode) {
							return true;
						}
						parent = parent.Parent;
					}
					return false;
				}
			}
		}

		/// <summary>
		/// Returns a more appropriate default size for initial drawing of the control at design time
		/// </summary>
		protected override Size DefaultSize {
			get { 
				return new Size(400, 200);
			}
		}

        [Description("The name of the remote desktop.")]
        /// <summary>
        /// The name of the remote desktop, or "Disconnected" if not connected.
        /// </summary>
        public string Hostname {
            get {
                return vnc == null ? "Disconnected" : vnc.HostName;
            }
        }

        /// <summary>
        /// The image of the remote desktop.
        /// </summary>
        public Image Desktop {
            get {
                return desktop;
            }
        }

		/// <summary>
		/// Get a complete update of the entire screen from the remote host.
		/// </summary>
		/// <remarks>You should allow users to call FullScreenUpdate in order to correct
		/// corruption of the local image.  This will simply request that the next update be
		/// for the full screen, and not a portion of it.  It will not do the update while
		/// blocking.
		/// </remarks>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the Connected state.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
		public void FullScreenUpdate()
		{
			InsureConnection(true);
			fullScreenRefresh = true;
		}

		/// <summary>
		/// Insures the state of the connection to the server, either Connected or Not Connected depending on the value of the connected argument.
		/// </summary>
		/// <param name="connected">True if the connection must be established, otherwise False.</param>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is in the wrong state.</exception>
		private void InsureConnection(bool connected)
		{
			// Grab the name of the calling routine:
			string methodName = new System.Diagnostics.StackTrace().GetFrame(1).GetMethod().Name;
			
			if (connected) {
				System.Diagnostics.Debug.Assert(state == RuntimeState.Connected || 
												state == RuntimeState.Disconnecting, // special case for Disconnect()
												string.Format("RemoteDesktop must be in RuntimeState.Connected before calling {0}.", methodName));
				if (state != RuntimeState.Connected && state != RuntimeState.Disconnecting) {
					throw new InvalidOperationException("RemoteDesktop must be in Connected state before calling methods that require an established connection.");
				}
			} else { // disconnected
				System.Diagnostics.Debug.Assert(state == RuntimeState.Disconnected,
												string.Format("RemoteDesktop must be in RuntimeState.Disconnected before calling {0}.", methodName));
                if (state != RuntimeState.Disconnected && state != RuntimeState.Disconnecting) {
					throw new InvalidOperationException("RemoteDesktop cannot be in Connected state when calling methods that establish a connection.");
				}
			}
		}

		// This event handler deals with Frambebuffer Updates coming from the host. An
		// EncodedRectangle object is passed via the VncEventArgs (actually an IDesktopUpdater
		// object so that *only* Draw() can be called here--Decode() is done elsewhere).
		// The VncClient object handles thread marshalling onto the UI thread.
		protected void VncUpdate(object sender, VncEventArgs e)
		{
			e.DesktopUpdater.Draw(desktop);
            Invalidate(desktopPolicy.AdjustUpdateRectangle(e.DesktopUpdater.UpdateRectangle));

			if (state == RuntimeState.Connected) {
				vnc.RequestScreenUpdate(fullScreenRefresh);
				
				// Make sure the next screen update is incremental
    			fullScreenRefresh = false;
			}
		}

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host)
        {
            // Use Display 0 by default.
            Connect(host, 0);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, bool viewOnly)
        {
            // Use Display 0 by default.
            Connect(host, 0, viewOnly);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <param name="scaled">Determines whether to use desktop scaling or leave it normal and clip.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, bool viewOnly, bool scaled)
        {
            // Use Display 0 by default.
            Connect(host, 0, viewOnly, scaled);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="display">The Display number (used on Unix hosts).</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, int display)
        {
            Connect(host, display, false);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="display">The Display number (used on Unix hosts).</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, int display, bool viewOnly)
        {
            Connect(host, display, viewOnly, false);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="display">The Display number (used on Unix hosts).</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <param name="scaled">Determines whether to use desktop scaling or leave it normal and clip.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, int display, bool viewOnly, bool scaled)
        {
            // TODO: Should this be done asynchronously so as not to block the UI?  Since an event 
            // indicates the end of the connection, maybe that would be a better design.
            InsureConnection(false);

            if (host == null) throw new ArgumentNullException("host");
            if (display < 0) throw new ArgumentOutOfRangeException("display", display, "Display number must be a positive integer.");

            // Start protocol-level handling and determine whether a password is needed
            vnc = new VncClient();
            vnc.ConnectionLost += new EventHandler(VncClientConnectionLost);
            vnc.ServerCutText += new EventHandler(VncServerCutText);

            passwordPending = vnc.Connect(host, display, VncPort, viewOnly);

            SetScalingMode(scaled);

            if (passwordPending) {
                // Server needs a password, so call which ever method is refered to by the GetPassword delegate.
                string password = GetPassword();

                if (password == null) {
                    // No password could be obtained (e.g., user clicked Cancel), so stop connecting
                    return;
                } else {
                    Authenticate(password);
                }
            } else {
                // No password needed, so go ahead and Initialize here
                Initialize();
            }
        }

		/// <summary>
		/// Authenticate with the VNC Host using a user supplied password.
		/// </summary>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
		/// <exception cref="System.NullReferenceException">Thrown if the password is null.</exception>
		/// <param name="password">The user's password.</param>
		public void Authenticate(string password)
		{
			InsureConnection(false);
			if (!passwordPending) throw new InvalidOperationException("Authentication is only required when Connect() returns True and the VNC Host requires a password.");
			if (password == null) throw new NullReferenceException("password");

			passwordPending = false;  // repeated calls to Authenticate should fail.
			if (vnc.Authenticate(password)) {
				Initialize();
			} else {		
				OnConnectionLost();										
			}	
		}

        /// <summary>
        /// Changes the input mode to view-only or interactive.
        /// </summary>
        /// <param name="viewOnly">True if view-only mode is desired (no mouse/keyboard events will be sent).</param>
        public void SetInputMode(bool viewOnly)
        {
            vnc.SetInputMode(viewOnly);
        }

        [DefaultValue(false)]
        [Description("True if view-only mode is desired (no mouse/keyboard events will be sent)")]
        /// <summary>
        /// True if view-only mode is desired (no mouse/keyboard events will be sent).
        /// </summary>
        public bool ViewOnly
        {
            get
            {
                return vnc.IsViewOnly;
            }
            set
            {
                SetInputMode(value);
            }
        }
        
        /// <summary>
        /// Set the remote desktop's scaling mode.
        /// </summary>
        /// <param name="scaled">Determines whether to use desktop scaling or leave it normal and clip.</param>
        public void SetScalingMode(bool scaled)
        {
            if (scaled) {
                desktopPolicy = new VncScaledDesktopPolicy(vnc, this);
            } else {
                desktopPolicy = new VncClippedDesktopPolicy(vnc, this);
            }

            AutoScroll = desktopPolicy.AutoScroll;
            AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;

            Invalidate();
        }

        [DefaultValue(false)]
        [Description("Determines whether to use desktop scaling or leave it normal and clip")]
        /// <summary>
        /// Determines whether to use desktop scaling or leave it normal and clip.
        /// </summary>
        public bool Scaled
        {
            get
            {
                return desktopPolicy.GetType() == typeof(VncScaledDesktopPolicy);
            }
            set
            {
                SetScalingMode(value);
            }
        }

		/// <summary>
		/// After protocol-level initialization and connecting is complete, the local GUI objects have to be set-up, and requests for updates to the remote host begun.
		/// </summary>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already in the Connected state.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>		
		protected void Initialize()
		{
			// Finish protocol handshake with host now that authentication is done.
			InsureConnection(false);
			vnc.Initialize();
			SetState(RuntimeState.Connected);
			
			// Create a buffer on which updated rectangles will be drawn and draw a "please wait..." 
			// message on the buffer for initial display until we start getting rectangles
			SetupDesktop();
	
			// Tell the user of this control the necessary info about the desktop in order to setup the display
			OnConnectComplete(new ConnectEventArgs(vnc.Framebuffer.Width,
												   vnc.Framebuffer.Height, 
												   vnc.Framebuffer.DesktopName));

            // Refresh scroll properties
            AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;

			// Start getting updates from the remote host (vnc.StartUpdates will begin a worker thread).
			vnc.VncUpdate += new VncUpdateHandler(VncUpdate);
			vnc.StartUpdates();
		}

		private void SetState(RuntimeState newState)
		{
			state = newState;
			
			// Set mouse pointer according to new state
			switch (state) {
				case RuntimeState.Connected:
					// Change the cursor to the "vnc" custor--a see-through dot
					Cursor = new Cursor(GetType(), "Resources.vnccursor.cur");
					break;
				// All other states should use the normal cursor.
				case RuntimeState.Disconnected:
				default:	
					Cursor = Cursors.Default;				
					break;
			}
		}

		/// <summary>
		/// Creates and initially sets-up the local bitmap that will represent the remote desktop image.
		/// </summary>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not already in the Connected state. See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
		protected void SetupDesktop()
		{
			InsureConnection(true);

			// Create a new bitmap to cache locally the remote desktop image.  Use the geometry of the
			// remote framebuffer, and 32bpp pixel format (doesn't matter what the server is sending--8,16,
			// or 32--we always draw 32bpp here for efficiency).
			desktop = new Bitmap(vnc.Framebuffer.Width, vnc.Framebuffer.Height, PixelFormat.Format32bppPArgb);
			
			// Draw a "please wait..." message on the local desktop until the first
			// rectangle(s) arrive and overwrite with the desktop image.
			DrawDesktopMessage("Connecting to VNC host, please wait...");
		}
	
		/// <summary>
		/// Draws the given message (white text) on the local desktop (all black).
		/// </summary>
		/// <param name="message">The message to be drawn.</param>
		protected void DrawDesktopMessage(string message)
		{
			System.Diagnostics.Debug.Assert(desktop != null, "Can't draw on desktop when null.");
			// Draw the given message on the local desktop
			using (Graphics g = Graphics.FromImage(desktop)) {
				g.FillRectangle(Brushes.Black, vnc.Framebuffer.Rectangle);

				StringFormat format = new StringFormat();
				format.Alignment = StringAlignment.Center;
				format.LineAlignment = StringAlignment.Center;

				g.DrawString(message, 
							 new Font("Arial", 12), 
							 new SolidBrush(Color.White), 
							 new PointF(vnc.Framebuffer.Width / 2, vnc.Framebuffer.Height / 2), format);
			}

		}
		
		/// <summary>
		/// Stops the remote host from sending further updates and disconnects.
		/// </summary>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not already in the Connected state. See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
		public void Disconnect()
		{
			InsureConnection(true);
			vnc.ConnectionLost -= new EventHandler(VncClientConnectionLost);
            vnc.ServerCutText -= new EventHandler(VncServerCutText);
			vnc.Disconnect();
			SetState(RuntimeState.Disconnected);
			OnConnectionLost();
			Invalidate();
		}

        /// <summary>
        /// Fills the remote server's clipboard with the text in the client's clipboard, if any.
        /// </summary>
        public void FillServerClipboard()
        {
            FillServerClipboard(Clipboard.GetText());
        }

        /// <summary>
        /// Fills the remote server's clipboard with text.
        /// </summary>
        /// <param name="text">The text to put in the server's clipboard.</param>
        public void FillServerClipboard(string text)
        {
            vnc.WriteClientCutText(text);
        }

		protected override void Dispose(bool disposing)
		{
			if (disposing) {
				// Make sure the connection is closed--should never happen :)
				if (state != RuntimeState.Disconnected) {
					Disconnect();
				}

				// See if either of the bitmaps used need clean-up.  
				if (desktop != null) desktop.Dispose();
				if (designModeDesktop != null) designModeDesktop.Dispose();
			}
			base.Dispose(disposing);
		}

		protected override void OnPaint(PaintEventArgs pe)
		{
			// If the control is in design mode, draw a nice background, otherwise paint the desktop.
			if (!DesignMode) {
				switch(state) {
					case RuntimeState.Connected:
						System.Diagnostics.Debug.Assert(desktop != null);
						DrawDesktopImage(desktop, pe.Graphics);
						break;
					case RuntimeState.Disconnected:
						// Do nothing, just black background.
						break;
					default:
						// Sanity check
						throw new NotImplementedException(string.Format("RemoteDesktop in unknown State: {0}.", state.ToString()));
				}
            } else {
				// Draw a static screenshot of a Windows desktop to simulate the control in action
				System.Diagnostics.Debug.Assert(designModeDesktop != null);
				DrawDesktopImage(designModeDesktop, pe.Graphics);
			}
			base.OnPaint(pe);
		}

        protected override void OnResize(EventArgs eventargs)
        {
            // Fix a bug with a ghost scrollbar in clipped mode on maximize
            Control parent = Parent;
            while (parent != null) {
                if (parent is Form) {
                    Form form = parent as Form;
                    if (form.WindowState == FormWindowState.Maximized)
                        form.Invalidate();
                    parent = null;
                } else {
                    parent = parent.Parent;
                }
            }

            base.OnResize(eventargs);
        }

		/// <summary>
		/// Draws an image onto the control in a size-aware way.
		/// </summary>
		/// <param name="desktopImage">The desktop image to be drawn to the control's sufrace.</param>
		/// <param name="g">The Graphics object representing the control's drawable surface.</param>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not already in the Connected state.</exception>
		protected void DrawDesktopImage(Image desktopImage, Graphics g)
		{
			g.DrawImage(desktopImage, desktopPolicy.RepositionImage(desktopImage));
		}

		/// <summary>
		/// RemoteDesktop listens for ConnectionLost events from the VncClient object.
		/// </summary>
		/// <param name="sender">The VncClient object that raised the event.</param>
		/// <param name="e">An empty EventArgs object.</param>
		protected void VncClientConnectionLost(object sender, EventArgs e)
		{
			// If the remote host dies, and there are attempts to write
			// keyboard/mouse/update notifications, this may get called 
			// many times, and from main or worker thread.
			// Guard against this and invoke Disconnect once.
			if (state == RuntimeState.Connected) {
				SetState(RuntimeState.Disconnecting);
				Disconnect();
			}
		}

        // Handle the VncClient ServerCutText event and bubble it up as ClipboardChanged.
        protected void VncServerCutText(object sender, EventArgs e)
        {
            OnClipboardChanged();
        }

        protected void OnClipboardChanged()
        {
            if (ClipboardChanged != null)
                ClipboardChanged(this, EventArgs.Empty);
        }

		/// <summary>
		/// Dispatches the ConnectionLost event if any targets have registered.
		/// </summary>
		/// <param name="e">An EventArgs object.</param>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is in the Connected state.</exception>
		protected void OnConnectionLost()
		{
			if (ConnectionLost != null) {
				ConnectionLost(this, EventArgs.Empty);
			}
		}
		
		/// <summary>
		/// Dispatches the ConnectComplete event if any targets have registered.
		/// </summary>
		/// <param name="e">A ConnectEventArgs object with information about the remote framebuffer's geometry.</param>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the Connected state.</exception>
		protected void OnConnectComplete(ConnectEventArgs e)
		{
			if (ConnectComplete != null) {
				ConnectComplete(this, e);
			}
		}

		// Handle Mouse Events:		 -------------------------------------------
		// In all cases, the same thing needs to happen: figure out where the cursor
		// is, and then figure out the state of all mouse buttons.
		// TODO: currently we don't handle the case of 3-button emulation with 2-buttons.
		protected override void OnMouseMove(MouseEventArgs mea)
		{
			UpdateRemotePointer();
			base.OnMouseMove(mea);
		}

		protected override void OnMouseDown(MouseEventArgs mea)
		{
            // BUG FIX (Edward Cooke) -- Deal with Control.Select() semantics
            if (!Focused) {
                Focus();
                Select();
            } else {
                UpdateRemotePointer();
            }
			base.OnMouseDown(mea);
		}
		
		// Find out the proper masks for Mouse Button Up Events
		protected override void OnMouseUp(MouseEventArgs mea)
		{
   			UpdateRemotePointer();
			base.OnMouseUp(mea);
		}
		
		// TODO: Perhaps overload UpdateRemotePointer to take a flag indicating if mousescroll has occured??
		protected override void OnMouseWheel(MouseEventArgs mea)
		{
			// HACK: this check insures that while in DesignMode, no messages are sent to a VNC Host
			// (i.e., there won't be one--NullReferenceException)			
            if (!DesignMode && IsConnected) {
				Point current = PointToClient(MousePosition);
				byte mask = 0;

				// mouse was scrolled forward
				if (mea.Delta > 0) {
					mask += 8;
				} else if (mea.Delta < 0) { // mouse was scrolled backwards
					mask += 16;
				}

				vnc.WritePointerEvent(mask, desktopPolicy.GetMouseMovePoint(current));
			}			
			base.OnMouseWheel(mea);
		}
		
		private void UpdateRemotePointer()
		{
			// HACK: this check insures that while in DesignMode, no messages are sent to a VNC Host
			// (i.e., there won't be one--NullReferenceException)			
			if (!DesignMode && IsConnected) {
				Point current = PointToClient(MousePosition);
				byte mask = 0;

				if (Control.MouseButtons == MouseButtons.Left)   mask += 1;
				if (Control.MouseButtons == MouseButtons.Middle) mask += 2;
				if (Control.MouseButtons == MouseButtons.Right)  mask += 4;

                Rectangle adjusted = desktopPolicy.GetMouseMoveRectangle();
                if (adjusted.Contains(current))
                    vnc.WritePointerEvent(mask, desktopPolicy.UpdateRemotePointer(current));
			}
		}

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        static extern int ToAscii(uint uVirtKey, uint uScanCode, byte[] lpKeyState, byte[] lpwTransKey, uint fuState);

        protected const int WM_CHAR = 0x0102;
        protected const int WM_KEYDOWN = 0x0100;
        protected const int WM_SYSKEYDOWN = 0x0104;
        protected const int WM_KEYUP = 0x0101;
        protected const int WM_SYSKEYUP = 0x0105;
        protected const int WM_IME_CHAR = 0x0286;

        protected const UInt16 VK_CONTROL = 0x0011;
        protected const UInt16 VK_MENU = 0x0012;
        protected const UInt16 VK_LCONTROL = 0x00A2;
        protected const UInt16 VK_RCONTROL = 0x00A3;
        protected const UInt16 VK_LMENU = 0x00A4;
        protected const UInt16 VK_RMENU = 0x00A5;

        protected const UInt16 XK_Control_L = 0xFFE3;
        protected const UInt16 XK_Control_R = 0xFFE4;
        protected const UInt16 XK_Meta_L = 0xFFE7;
        protected const UInt16 XK_Meta_R = 0xFFE8;
        protected const UInt16 XK_Alt_L = 0xFFE9;
        protected const UInt16 XK_Alt_R = 0xFFEA;

        protected const byte KEY_STATE_DOWN = 0x80;

        [SecurityPermissionAttribute(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        [SecurityPermissionAttribute(SecurityAction.InheritanceDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        protected override bool ProcessKeyEventArgs(ref Message m)
        {
            if (DesignMode || !IsConnected || !(m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN))
                return base.ProcessKeyEventArgs(ref m);

            var keyboardState = new byte[256];
            if (!GetKeyboardState(keyboardState))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var maskedKeyboardState = new byte[256];
            keyboardState.CopyTo(maskedKeyboardState, 0);
            maskedKeyboardState[VK_CONTROL] = 0;
            maskedKeyboardState[VK_LCONTROL] = 0;
            maskedKeyboardState[VK_RCONTROL] = 0;
            maskedKeyboardState[VK_MENU] = 0;
            maskedKeyboardState[VK_LMENU] = 0;
            maskedKeyboardState[VK_RMENU] = 0;

            var virtualKey = Convert.ToUInt16(m.WParam.ToInt32());
            var rfbKeyCode = TranslateVirtualKey(virtualKey);

            var charResult = new byte[2];
            var charCount = ToAscii(virtualKey, MapVirtualKey(virtualKey, 0), maskedKeyboardState, charResult, 0);

            if (rfbKeyCode == virtualKey && charCount > 0)
                rfbKeyCode = Convert.ToUInt16(charResult[0]);

            UInt16 modifierKeyCode = 0;
            if ((keyboardState[VK_RCONTROL] & KEY_STATE_DOWN) != 0)
                modifierKeyCode = XK_Control_R; // Right Control
            else if ((keyboardState[VK_LCONTROL] & KEY_STATE_DOWN) != 0)
                modifierKeyCode = XK_Control_L; // Left Control

            if ((keyboardState[VK_RMENU] & KEY_STATE_DOWN) != 0)
                // check right Alt first to ensure AltGr is processed correctly
                modifierKeyCode = XK_Alt_R; // Right Alt
            else if ((keyboardState[VK_LMENU] & KEY_STATE_DOWN) != 0)
                modifierKeyCode = XK_Alt_L; // Left Alt

            if (modifierKeyCode != 0) vnc.WriteKeyboardEvent(modifierKeyCode, true);
            vnc.WriteKeyboardEvent(Convert.ToUInt16(rfbKeyCode), true);
            vnc.WriteKeyboardEvent(Convert.ToUInt16(rfbKeyCode), false);
            if (modifierKeyCode != 0) vnc.WriteKeyboardEvent(modifierKeyCode, false);

            return true;
        }

        protected static Dictionary<UInt16, UInt16> KeyTranslationTable = new Dictionary<UInt16, UInt16>
        {
            { 0x0003, 0xFF69 }, // VK_CANCEL    XK_Cancel
            { 0x0008, 0xFF08 }, // VK_BACK      XK_BackSpace
            { 0x0009, 0xFF09 }, // VK_TAB       XK_Tab
            { 0x000C, 0xFF0B }, // VK_CLEAR     XK_Clear
            { 0x000D, 0xFF0D }, // VK_RETURN    XK_Return
            { 0x0013, 0xFF13 }, // VK_PAUSE     XK_Pause
            { 0x001B, 0xFF1B }, // VK_ESCAPE    XK_Escape
            { 0x002C, 0xFF15 }, // VK_SNAPSHOT  XK_Sys_Req
            { 0x002D, 0xFF63 }, // VK_INSERT    XK_Insert
            { 0x002E, 0xFFFF }, // VK_DELETE    XK_Delete
            { 0x0024, 0xFF50 }, // VK_HOME      XK_Home
            { 0x0023, 0xFF57 }, // VK_END       XK_End
            { 0x0021, 0xFF55 }, // VK_PRIOR     XK_Prior        Page Up
            { 0x0022, 0xFF56 }, // VK_NEXT      XK_Next         Page Down
            { 0x0025, 0xFF51 }, // VK_LEFT      XK_Left
            { 0x0026, 0xFF52 }, // VK_UP        XK_Up
            { 0x0027, 0xFF53 }, // VK_RIGHT     XK_Right
            { 0x0028, 0xFF54 }, // VK_DOWN      XK_Down
            { 0x0029, 0xFF60 }, // VK_SELECT    XK_Select
            { 0x002A, 0xFF61 }, // VK_PRINT     XK_Print
            { 0x002B, 0xFF62 }, // VK_EXECUTE   XK_Execute
            { 0x002F, 0xFF6A }, // VK_HELP      XK_Help
          //{ 0x0000, 0xFF6B }, //              XK_Break
            { 0x0070, 0xFFBE }, // VK_F1        XK_F1
            { 0x0071, 0xFFBF }, // VK_F2        XK_F2
            { 0x0072, 0xFFC0 }, // VK_F3        XK_F3
            { 0x0073, 0xFFC1 }, // VK_F4        XK_F4
            { 0x0074, 0xFFC2 }, // VK_F5        XK_F5
            { 0x0075, 0xFFC3 }, // VK_F6        XK_F6
            { 0x0076, 0xFFC4 }, // VK_F7        XK_F7
            { 0x0077, 0xFFC5 }, // VK_F8        XK_F8
            { 0x0078, 0xFFC6 }, // VK_F9        XK_F9
            { 0x0079, 0xFFC7 }, // VK_F10       XK_F10
            { 0x007A, 0xFFC8 }, // VK_F11       XK_F11
            { 0x007B, 0xFFC9 }, // VK_F12       XK_F12
            { 0x0010, 0xFFE1 }, // VK_SHIFT
            { 0x00A0, 0xFFE1 }, // VK_LSHIFT    XK_Shift_L
            { 0x00A1, 0xFFE2 }, // VK_RSHIFT    XK_Shift_R
            { 0x0011, 0xFFE3 }, // VK_CONTROL
            { 0x00A2, 0xFFE3 }, // VK_LCONTROL  XK_Control_L
            { 0x00A3, 0xFFE4 }, // VK_RCONTROL  XK_Control_R
            { 0x0012, 0xFFE9 }, // VK_MENU                      Alt
            { 0x00A4, 0xFFE9 }, // VK_LMENU     XK_Alt_L        Left Alt
            { 0x00A5, 0xFFEA }, // VK_RMENU     XK_Alt_R        Right Alt
            { 0x005B, 0xFFEB }, // VK_LWIN      XK_Super_L      Left Windows Key
            { 0x005C, 0xFFEC }, // VK_RWIN      XK_Super_R      Right Windows Key
            { 0x005D, 0xFF67 }, // VK_APPS      XK_Menu         Menu Key
          //{ 0x0000, 0xFFE7 }, //              XK_Meta_L
          //{ 0x0000, 0xFFE8 }, //              XK_Meta_R
          //{ 0x0000, 0xFFED }, //              XK_Hyper_L
          //{ 0x0000, 0xFFEE }, //              XK_Hyper_R
        };

        public static UInt16 TranslateVirtualKey(UInt16 virtualKey)
        {
            if (KeyTranslationTable.ContainsKey(virtualKey))
                return KeyTranslationTable[virtualKey];
            else
                return virtualKey;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            return ProcessKeyEventArgs(ref msg);
        }

		/// <summary>
		/// Sends a keyboard combination that would otherwise be reserved for the client PC.
		/// </summary>
		/// <param name="keys">SpecialKeys is an enumerated list of supported keyboard combinations.</param>
		/// <remarks>Keyboard combinations are Pressed and then Released, while single keys (e.g., SpecialKeys.Ctrl) are only pressed so that subsequent keys will be modified.</remarks>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the Connected state.</exception>
		public void SendSpecialKeys(SpecialKeys keys)
		{
			this.SendSpecialKeys(keys, true);
		}

		/// <summary>
		/// Sends a keyboard combination that would otherwise be reserved for the client PC.
		/// </summary>
		/// <param name="keys">SpecialKeys is an enumerated list of supported keyboard combinations.</param>
		/// <remarks>Keyboard combinations are Pressed and then Released, while single keys (e.g., SpecialKeys.Ctrl) are only pressed so that subsequent keys will be modified.</remarks>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the Connected state.</exception>
		public void SendSpecialKeys(SpecialKeys keys, bool release)
		{
			InsureConnection(true);
			// For all of these I am sending the key presses manually instead of calling
			// the keyboard event handlers, as I don't want to propegate the calls up to the 
			// base control class and form.
			switch(keys) {
				case SpecialKeys.Ctrl:
					PressKeys(new uint[] { 0xffe3 }, release);	// CTRL, but don't release
					break;
				case SpecialKeys.Alt:
					PressKeys(new uint[] { 0xffe9 }, release);	// ALT, but don't release
					break;
				case SpecialKeys.CtrlAltDel:
					PressKeys(new uint[] { 0xffe3, 0xffe9, 0xffff }, release); // CTRL, ALT, DEL
					break;
				case SpecialKeys.AltF4:
					PressKeys(new uint[] { 0xffe9, 0xffc1 }, release); // ALT, F4
					break;					
				case SpecialKeys.CtrlEsc:
					PressKeys(new uint[] { 0xffe3, 0xff1b }, release); // CTRL, ESC
					break;
				// TODO: are there more I should support???
				default:
					break;
			}
		}
		
		/// <summary>
		/// Given a list of keysym values, sends a key press for each, then a release.
		/// </summary>
		/// <param name="keys">An array of keysym values representing keys to press/release.</param>
		/// <param name="release">A boolean indicating whether the keys should be Pressed and then Released.</param>
		private void PressKeys(uint[] keys, bool release)
		{
			System.Diagnostics.Debug.Assert(keys != null, "keys[] cannot be null.");
			
			for(int i = 0; i < keys.Length; ++i) {
				vnc.WriteKeyboardEvent(keys[i], true);
			}
			
			if (release) {
				// Walk the keys array backwards in order to release keys in correct order
				for(int i = keys.Length - 1; i >= 0; --i) {
					vnc.WriteKeyboardEvent(keys[i], false);
				}
			}
		}
	}
}