﻿namespace ZeroMQ
{
    using System;

    /// <summary>
    /// A single message-part to be sent or received via a <see cref="ZmqSocket"/>.
    /// </summary>
    /// <remarks>
    /// The <see cref="Frame"/> class has a one-to-one correspondence with the native <c>zmq_msg_t</c> struct.
    /// </remarks>
    public class Frame
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Frame"/> class. The contents of <paramref name="buffer"/>
        /// will be copied into the resulting <see cref="Frame"/>.
        /// </summary>
        /// <param name="buffer">A <see cref="byte"/> array containing the frame data to copy.</param>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is null.</exception>
        public Frame(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }

            Buffer = new byte[buffer.Length];
            buffer.CopyTo(Buffer, 0);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Frame"/> class. The resulting <see cref="Frame"/> will have
        /// a buffer size equal to <paramref name="length"/>.
        /// </summary>
        /// <param name="length">An <see cref="int"/> containing the frame buffer length.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
        public Frame(int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException("length", "A non-negative value is expected.");
            }

            Buffer = new byte[length];
        }

        /// <summary>
        /// Gets the underlying frame data buffer.
        /// </summary>
        public byte[] Buffer { get; internal set; }

        /// <summary>
        /// Gets the maximum size of the frame buffer.
        /// </summary>
        public int BufferSize
        {
            get { return Buffer.Length; }
        }

        /// <summary>
        /// Gets the size of the last received message.
        /// </summary>
        public int MessageSize { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether more frames can be received in the multi-part message sequence.
        /// </summary>
        public bool HasMore { get; internal set; }

        /// <summary>
        /// Gets the status of the last Recieve operation.
        /// </summary>
        public ReceiveStatus ReceiveStatus { get; internal set; }

        /// <summary>
        /// Converts a <see cref="Frame"/> to a <see cref="byte"/> array.
        /// </summary>
        /// <param name="frame">The <see cref="Frame"/> to convert.</param>
        /// <returns>The data contained by <paramref name="frame"/>.</returns>
        public static explicit operator byte[](Frame frame)
        {
            return frame.Buffer;
        }
    }
}
