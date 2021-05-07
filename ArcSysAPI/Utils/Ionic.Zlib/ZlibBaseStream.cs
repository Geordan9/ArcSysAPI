// ZlibBaseStream.cs
// ------------------------------------------------------------------
//
// Copyright (c) 2009 Dino Chiesa and Microsoft Corporation.
// All rights reserved.
//
// This code module is part of DotNetZip, a zipfile class library.
//
// ------------------------------------------------------------------
//
// This code is licensed under the Microsoft Public License.
// See the file License.txt for the license details.
// More info on: http://dotnetzip.codeplex.com
//
// ------------------------------------------------------------------
//
// last saved (in emacs):
// Time-stamp: <2011-August-06 21:22:38>
//
// ------------------------------------------------------------------
//
// This module defines the ZlibBaseStream class, which is an intnernal
// base class for DeflateStream, ZlibStream and GZipStream.
//
// ------------------------------------------------------------------

using System;
using System.IO;
using System.Text;

namespace Ionic.Zlib
{
    internal enum ZlibStreamFlavor
    {
        ZLIB = 1950,
        DEFLATE = 1951
    }

    internal class ZlibBaseStream : Stream
    {
        protected internal ZlibCodec _z; // deferred init... new ZlibCodec();

        protected internal StreamMode _streamMode = StreamMode.Undefined;
        protected internal FlushType _flushMode;
        protected internal ZlibStreamFlavor _flavor;
        protected internal CompressionMode _compressionMode;
        protected internal CompressionLevel _level;
        protected internal bool _leaveOpen;
        protected internal byte[] _workingBuffer;
        protected internal int _bufferSize = ZlibConstants.WorkingBufferSizeDefault;
        protected internal byte[] _buf1 = new byte[1];

        protected internal Stream _stream;
        protected internal CompressionStrategy Strategy = CompressionStrategy.Default;

        public ZlibBaseStream(Stream stream,
            CompressionMode compressionMode,
            CompressionLevel level,
            ZlibStreamFlavor flavor,
            bool leaveOpen)
        {
            _flushMode = FlushType.None;
            //this._workingBuffer = new byte[WORKING_BUFFER_SIZE_DEFAULT];
            _stream = stream;
            _leaveOpen = leaveOpen;
            _compressionMode = compressionMode;
            _flavor = flavor;
            _level = level;
        }


        protected internal bool _wantCompress => _compressionMode == CompressionMode.Compress;

        private ZlibCodec z
        {
            get
            {
                if (_z == null)
                {
                    var wantRfc1950Header = _flavor == ZlibStreamFlavor.ZLIB;
                    _z = new ZlibCodec();
                    if (_compressionMode == CompressionMode.Decompress)
                    {
                        _z.InitializeInflate(wantRfc1950Header);
                    }
                    else
                    {
                        _z.Strategy = Strategy;
                        _z.InitializeDeflate(_level, wantRfc1950Header);
                    }
                }

                return _z;
            }
        }


        private byte[] workingBuffer
        {
            get
            {
                if (_workingBuffer == null)
                    _workingBuffer = new byte[_bufferSize];
                return _workingBuffer;
            }
        }


        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_streamMode == StreamMode.Undefined)
                _streamMode = StreamMode.Writer;
            else if (_streamMode != StreamMode.Writer)
                throw new ZlibException("Cannot Write after Reading.");

            if (count == 0)
                return;

            // first reference of z property will initialize the private var _z
            z.InputBuffer = buffer;
            _z.NextIn = offset;
            _z.AvailableBytesIn = count;
            var done = false;
            do
            {
                _z.OutputBuffer = workingBuffer;
                _z.NextOut = 0;
                _z.AvailableBytesOut = _workingBuffer.Length;
                var rc = _wantCompress
                    ? _z.Deflate(_flushMode)
                    : _z.Inflate(_flushMode);
                if (rc != ZlibConstants.Z_OK && rc != ZlibConstants.Z_STREAM_END)
                    throw new ZlibException((_wantCompress ? "de" : "in") + "flating: " + _z.Message);

                //if (_workingBuffer.Length - _z.AvailableBytesOut > 0)
                _stream.Write(_workingBuffer, 0, _workingBuffer.Length - _z.AvailableBytesOut);

                done = _z.AvailableBytesIn == 0 && _z.AvailableBytesOut != 0;
            } while (!done);
        }


        private void finish()
        {
            if (_z == null) return;

            if (_streamMode == StreamMode.Writer)
            {
                var done = false;
                do
                {
                    _z.OutputBuffer = workingBuffer;
                    _z.NextOut = 0;
                    _z.AvailableBytesOut = _workingBuffer.Length;
                    var rc = _wantCompress
                        ? _z.Deflate(FlushType.Finish)
                        : _z.Inflate(FlushType.Finish);

                    if (rc != ZlibConstants.Z_STREAM_END && rc != ZlibConstants.Z_OK)
                    {
                        var verb = (_wantCompress ? "de" : "in") + "flating";
                        if (_z.Message == null)
                            throw new ZlibException(string.Format("{0}: (rc = {1})", verb, rc));
                        throw new ZlibException(verb + ": " + _z.Message);
                    }

                    if (_workingBuffer.Length - _z.AvailableBytesOut > 0)
                        _stream.Write(_workingBuffer, 0, _workingBuffer.Length - _z.AvailableBytesOut);

                    done = _z.AvailableBytesIn == 0 && _z.AvailableBytesOut != 0;
                } while (!done);

                Flush();
            }
        }


        private void end()
        {
            if (z == null)
                return;
            if (_wantCompress)
                _z.EndDeflate();
            else
                _z.EndInflate();
            _z = null;
        }


        public override void Close()
        {
            if (_stream == null) return;
            try
            {
                finish();
            }
            finally
            {
                end();
                if (!_leaveOpen) _stream.Close();
                _stream = null;
            }
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
            //_outStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(value);
        }


#if NOT
        public int Read()
        {
            if (Read(_buf1, 0, 1) == 0)
                return 0;
            // calculate CRC after reading
            if (crc!=null)
                crc.SlurpBlock(_buf1,0,1);
            return (_buf1[0] & 0xFF);
        }
#endif

        private bool nomoreinput;


        public override int Read(byte[] buffer, int offset, int count)
        {
            // According to MS documentation, any implementation of the IO.Stream.Read function must:
            // (a) throw an exception if offset & count reference an invalid part of the buffer,
            //     or if count < 0, or if buffer is null
            // (b) return 0 only upon EOF, or if count = 0
            // (c) if not EOF, then return at least 1 byte, up to <count> bytes

            if (_streamMode == StreamMode.Undefined)
            {
                if (!_stream.CanRead) throw new ZlibException("The stream is not readable.");
                // for the first read, set up some controls.
                _streamMode = StreamMode.Reader;
                // (The first reference to _z goes through the private accessor which
                // may initialize it.)
                z.AvailableBytesIn = 0;
            }

            if (_streamMode != StreamMode.Reader)
                throw new ZlibException("Cannot Read after Writing.");

            if (count == 0) return 0;
            if (nomoreinput && _wantCompress) return 0; // workitem 8557
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            if (offset < buffer.GetLowerBound(0)) throw new ArgumentOutOfRangeException("offset");
            if (offset + count > buffer.GetLength(0)) throw new ArgumentOutOfRangeException("count");

            var rc = 0;

            // set up the output of the deflate/inflate codec:
            _z.OutputBuffer = buffer;
            _z.NextOut = offset;
            _z.AvailableBytesOut = count;

            // This is necessary in case _workingBuffer has been resized. (new byte[])
            // (The first reference to _workingBuffer goes through the private accessor which
            // may initialize it.)
            _z.InputBuffer = workingBuffer;

            do
            {
                // need data in _workingBuffer in order to deflate/inflate.  Here, we check if we have any.
                if (_z.AvailableBytesIn == 0 && !nomoreinput)
                {
                    // No data available, so try to Read data from the captive stream.
                    _z.NextIn = 0;
                    _z.AvailableBytesIn = _stream.Read(_workingBuffer, 0, _workingBuffer.Length);
                    if (_z.AvailableBytesIn == 0)
                        nomoreinput = true;
                }

                // we have data in InputBuffer; now compress or decompress as appropriate
                rc = _wantCompress
                    ? _z.Deflate(_flushMode)
                    : _z.Inflate(_flushMode);

                if (nomoreinput && rc == ZlibConstants.Z_BUF_ERROR)
                    return 0;

                if (rc != ZlibConstants.Z_OK && rc != ZlibConstants.Z_STREAM_END)
                    throw new ZlibException(string.Format("{0}flating:  rc={1}  msg={2}", _wantCompress ? "de" : "in",
                        rc, _z.Message));

                if ((nomoreinput || rc == ZlibConstants.Z_STREAM_END) && _z.AvailableBytesOut == count)
                    break; // nothing more to read
            }
            //while (_z.AvailableBytesOut == count && rc == ZlibConstants.Z_OK);
            while (_z.AvailableBytesOut > 0 && !nomoreinput && rc == ZlibConstants.Z_OK);


            // workitem 8557
            // is there more room in output?
            if (_z.AvailableBytesOut > 0)
            {
                if (rc == ZlibConstants.Z_OK && _z.AvailableBytesIn == 0)
                {
                    // deferred
                }

                // are we completely done reading?
                if (nomoreinput)
                    // and in compression?
                    if (_wantCompress)
                    {
                        // no more input data available; therefore we flush to
                        // try to complete the read
                        rc = _z.Deflate(FlushType.Finish);

                        if (rc != ZlibConstants.Z_OK && rc != ZlibConstants.Z_STREAM_END)
                            throw new ZlibException(string.Format("Deflating:  rc={0}  msg={1}", rc, _z.Message));
                    }
            }


            rc = count - _z.AvailableBytesOut;

            return rc;
        }


        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _stream.Length;

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        internal enum StreamMode
        {
            Writer,
            Reader,
            Undefined
        }


        public static void CompressString(string s, Stream compressor)
        {
            var uncompressed = Encoding.UTF8.GetBytes(s);
            using (compressor)
            {
                compressor.Write(uncompressed, 0, uncompressed.Length);
            }
        }

        public static void CompressBuffer(byte[] b, Stream compressor)
        {
            // workitem 8460
            using (compressor)
            {
                compressor.Write(b, 0, b.Length);
            }
        }

        public static string UncompressString(byte[] compressed, Stream decompressor)
        {
            // workitem 8460
            var working = new byte[1024];
            var encoding = Encoding.UTF8;
            using (var output = new MemoryStream())
            {
                using (decompressor)
                {
                    int n;
                    while ((n = decompressor.Read(working, 0, working.Length)) != 0) output.Write(working, 0, n);
                }

                // reset to allow read from start
                output.Seek(0, SeekOrigin.Begin);
                var sr = new StreamReader(output, encoding);
                return sr.ReadToEnd();
            }
        }

        public static byte[] UncompressBuffer(byte[] compressed, Stream decompressor)
        {
            // workitem 8460
            var working = new byte[1024];
            using (var output = new MemoryStream())
            {
                using (decompressor)
                {
                    int n;
                    while ((n = decompressor.Read(working, 0, working.Length)) != 0) output.Write(working, 0, n);
                }

                return output.ToArray();
            }
        }
    }
}