﻿#region --- License ---
/* Copyright (c) 2006, 2007 Stefanos Apostolopoulos
 * See license.txt for license info
 */
#endregion

#region --- Using directives ---

using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Forms;
using OpenTK.Input;

#endregion

namespace OpenTK.Platform.Windows
{
    internal class WinRawInput : NativeWindow, IInputDriver
    {
        /// <summary>
        /// Input event data.
        /// </summary>
        private RawInput data = new RawInput();
        /// <summary>
        /// The total number of input devices connected to this system.
        /// </summary>
        private static int deviceCount;
        int rawInputStructSize = API.RawInputSize;

        private WinRawKeyboard keyboardDriver;
        private WinRawMouse mouseDriver;

        #region --- Constructors ---

        internal WinRawInput(IWindowInfo parent)
        {
            Debug.WriteLine("Initalizing windows raw input driver.");
            Debug.Indent();

            AssignHandle(parent.Handle);
            Debug.Print("Input window attached to parent {0}", parent);
            keyboardDriver = new WinRawKeyboard(this.Handle);
            mouseDriver = new WinRawMouse(this.Handle);
            
            Debug.Unindent();

            AllocateBuffer();
        }

        #endregion

        #region internal static int DeviceCount

        internal static int DeviceCount
        {
            get
            {
                API.GetRawInputDeviceList(null, ref deviceCount, API.RawInputDeviceListSize);
                return deviceCount;
            }
        }

        #endregion

        #region protected override void WndProc(ref Message msg)

        /// <summary>
        /// Processes the input Windows Message, routing the data to the correct Keyboard, Mouse or HID.
        /// </summary>
        /// <param name="msg">The WM_INPUT message, containing the data on the input event.</param>
        protected override void WndProc(ref Message msg)
        {
            switch ((WindowMessage)msg.Msg)
            {
                case WindowMessage.INPUT:
                    int size = 0;
                    // Get the size of the input data
                    API.GetRawInputData(msg.LParam, GetRawInputDataEnum.INPUT,
                        IntPtr.Zero, ref size, API.RawInputHeaderSize);

                    //if (data == null || API.RawInputSize < size)
                    //{
                    //    throw new ApplicationException("Critical error when processing raw windows input.");
                    //}
                    if (size == API.GetRawInputData(msg.LParam, GetRawInputDataEnum.INPUT,
                            data, ref size, API.RawInputHeaderSize))
                    {
                        switch (data.Header.Type)
                        {
                            case RawInputDeviceType.KEYBOARD:
                                if (!keyboardDriver.ProcessKeyboardEvent(data))
                                    API.DefRawInputProc(ref data, 1, (uint)API.RawInputHeaderSize);
                                return;

                            case RawInputDeviceType.MOUSE:
                                if (!mouseDriver.ProcessEvent(data))
                                    API.DefRawInputProc(ref data, 1, (uint)API.RawInputHeaderSize);
                                return;

                            case RawInputDeviceType.HID:
                                API.DefRawInputProc(ref data, 1, (uint)API.RawInputHeaderSize);
                                return;

                            default:
                                break;
                        }
                    }
                    else
                    {
                        throw new ApplicationException(String.Format(
                            "GetRawInputData returned invalid data. Windows error {0}. Please file a bug at http://opentk.sourceforge.net",
                            Marshal.GetLastWin32Error()));
                    }
                    break;

                case WindowMessage.CLOSE:
                case WindowMessage.DESTROY:
                    Debug.Print("Input window detached from parent {0}.", Handle);
                    ReleaseHandle();
                    break;

                case WindowMessage.QUIT:
                    Debug.WriteLine("Input window quit.");
                    this.Dispose();
                    break;
            }

            base.WndProc(ref msg);
        }

        #endregion

        #region --- IInputDriver Members ---

        public IList<Keyboard> Keyboard
        {
            get { return keyboardDriver.Keyboard; }
        }

        public IList<Mouse> Mouse
        {
            get { return mouseDriver.Mouse; }
        }

        int allocated_buffer_size;  // rin_data size in bytes.
        IntPtr rin_data;        // Unmanaged buffer with grow-only behavior. Freed at Dispose(bool).

        /// <summary>
        /// Allocates a buffer for buffered reading of RawInput structs. Starts at 16*sizeof(RawInput) and
        /// doubles the buffer every call thereafter.
        /// </summary>
        private void AllocateBuffer()
        {
            // Find the size of the buffer (grow-only).
            if (allocated_buffer_size == 0)
            {
                allocated_buffer_size = 16536 * rawInputStructSize;
            }
            else
            {
                allocated_buffer_size *= 2;
            }

            // Allocate the new buffer.
            if (rin_data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(rin_data);
            }
            rin_data = Marshal.AllocHGlobal(allocated_buffer_size);
            if (rin_data == IntPtr.Zero)
            {
                throw new OutOfMemoryException(String.Format(
                    "Failed to allocate {0} bytes for raw input structures.", allocated_buffer_size));
            }
        }

        public void Poll()
        {
            return;
            // We will do a buffered read for all input devices and route the RawInput structures
            // to the correct 'ProcessData' handlers. First, we need to find out the size of the
            // buffer to allocate for the structures. Then we allocate the buffer and read the
            // structures, calling the correct handler for each one. Last, we free the allocated
            // buffer.
            while (true)
            {
                // Iterate reading all available RawInput structures and routing them to their respective
                // handlers.
                int num = API.GetRawInputBuffer(rin_data, ref allocated_buffer_size, API.RawInputHeaderSize);
                if (num == 0)
                    return;
                else if (num < 0)
                {
                    /*int error = Marshal.GetLastWin32Error();
                    if (error == 122)
                    {
                        // Enlarge the buffer, it was too small.
                        AllocateBuffer();
                    }
                    else
                    {
                        throw new ApplicationException(String.Format(
                            "GetRawInputBuffer failed with code: {0}", error));
                    }*/
                    Debug.Print("GetRawInputBuffer failed with code: {0}", Marshal.GetLastWin32Error());
                    //AllocateBuffer();
                    return;
                }

                IntPtr next_rin = rin_data;
                int i = num;
                while (--i > 0)
                {
                    RawInput rin;
                    rin = (RawInput)Marshal.PtrToStructure(next_rin, typeof(RawInput));
                    if (rin.Header.Type == RawInputDeviceType.KEYBOARD)
                        keyboardDriver.ProcessKeyboardEvent(rin);
                    else if (rin.Header.Type == RawInputDeviceType.MOUSE)
                        mouseDriver.ProcessEvent(rin);
                    next_rin = API.NextRawInputStructure(next_rin);
                }
                API.DefRawInputProc(rin_data, num, (uint)API.RawInputHeaderSize);
            }
        }

        #endregion

        #region --- IDisposable Members ---

        private bool disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool manual)
        {
            if (!disposed)
            {
                if (rin_data != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(rin_data);
                }

                if (manual)
                {
                    keyboardDriver.Dispose();
                    this.ReleaseHandle();
                }

                disposed = true;
            }
        }

        ~WinRawInput()
        {
            Dispose(false);
        }

        #endregion
    }
}
