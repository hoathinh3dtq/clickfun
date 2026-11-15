// Helper điều khiển máy Canon qua EDSDK cho bản booth Windows (thay digiCamControl).
//
// Vì sao có file này: digiCamControl bấm hết nút để chụp, trượt nét thì ném lỗi luôn mà
// KHÔNG nhả nút → R50 giữ nút và báo bận (Canon 0x81) tới khi tắt/bật nguồn (đo 18/10/2026).
// Ở đây mọi lần bấm nút đều được nhả (ShutterButton_OFF) trong finally, trượt nét hay không.
//
// App biên dịch file này bằng csc.exe có sẵn trong .NET Framework 4.x của Windows (chỉ hiểu
// C# 5 — không dùng $"", ?., nameof…), bản x86 vì EDSDK.dll đi kèm digiCamControl là 32-bit.
//
// Giao thức: mỗi dòng stdin là một lệnh JSON {"id":1,"cmd":"status",...}; mỗi dòng stdout là
// {"id":1,"ok":true,"result":{...}} hoặc {"id":1,"ok":false,"code":"...","eds":123,"message":"..."}.
// Sự kiện không hỏi mà có: {"event":"disconnected"}.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CamRentBooth
{
    internal static class Eds
    {
        public const uint OK = 0x0;
        public const uint ERR_DEVICE_BUSY = 0x81;
        public const uint ERR_TAKE_PICTURE_AF_NG = 0x8D01;
        public const uint ERR_OBJECT_NOTREADY = 0xA102;

        public const uint PropID_ProductName = 0x2;
        public const uint PropID_BatteryLevel = 0x8;
        public const uint PropID_SaveTo = 0xB;
        public const uint PropID_BodyIDEx = 0x15;
        public const uint PropID_Evf_OutputDevice = 0x500;
        public const uint PropID_Evf_Mode = 0x501;

        public const uint SaveTo_Both = 3;
        public const uint EvfOutputDevice_TFT = 1;
        public const uint EvfOutputDevice_PC = 2;

        public const uint CameraCommand_PressShutterButton = 0x4;
        public const uint CameraCommand_DriveLensEvf = 0x103;
        public const int ShutterButton_OFF = 0x0;
        public const int ShutterButton_Completely = 0x3;
        public const int ShutterButton_Completely_NonAF = 0x10003;

        public const uint ObjectEvent_All = 0x200;
        public const uint ObjectEvent_DirItemRequestTransfer = 0x208;
        public const uint StateEvent_All = 0x300;
        public const uint StateEvent_Shutdown = 0x301;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szPortName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szDeviceDescription;
            public uint DeviceSubType;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DirectoryItemInfo
        {
            public ulong Size;
            public int IsFolder;
            public uint GroupID;
            public uint Option;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szFileName;
            public uint Format;
            public uint DateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PropertyDesc
        {
            public int Form;
            public int Access;
            public int NumElements;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public int[] PropDesc;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Capacity
        {
            public int NumberOfFreeClusters;
            public int BytesPerSector;
            public int Reset;
        }

        public delegate uint ObjectEventHandler(uint inEvent, IntPtr inRef, IntPtr inContext);
        public delegate uint StateEventHandler(uint inEvent, uint inParameter, IntPtr inContext);

        [DllImport("EDSDK.dll")] public static extern uint EdsInitializeSDK();
        [DllImport("EDSDK.dll")] public static extern uint EdsTerminateSDK();
        [DllImport("EDSDK.dll")] public static extern uint EdsGetEvent();
        [DllImport("EDSDK.dll")] public static extern uint EdsRelease(IntPtr inRef);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetCameraList(out IntPtr outCameraListRef);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetChildCount(IntPtr inRef, out int outCount);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetChildAtIndex(IntPtr inRef, int inIndex, out IntPtr outRef);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetDeviceInfo(IntPtr inCameraRef, out DeviceInfo outDeviceInfo);
        [DllImport("EDSDK.dll")] public static extern uint EdsOpenSession(IntPtr inCameraRef);
        [DllImport("EDSDK.dll")] public static extern uint EdsCloseSession(IntPtr inCameraRef);
        [DllImport("EDSDK.dll")] public static extern uint EdsSendCommand(IntPtr inCameraRef, uint inCommand, int inParam);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetPropertySize(IntPtr inRef, uint inPropertyID, int inParam, out uint outDataType, out int outSize);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetPropertyData(IntPtr inRef, uint inPropertyID, int inParam, int inPropertySize, out uint outPropertyData);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetPropertyData(IntPtr inRef, uint inPropertyID, int inParam, int inPropertySize, IntPtr outPropertyData);
        [DllImport("EDSDK.dll")] public static extern uint EdsSetPropertyData(IntPtr inRef, uint inPropertyID, int inParam, int inPropertySize, ref uint inPropertyData);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetPropertyDesc(IntPtr inRef, uint inPropertyID, out PropertyDesc outPropertyDesc);
        [DllImport("EDSDK.dll")] public static extern uint EdsSetCapacity(IntPtr inCameraRef, Capacity inCapacity);
        [DllImport("EDSDK.dll")] public static extern uint EdsSetObjectEventHandler(IntPtr inCameraRef, uint inEvent, ObjectEventHandler inHandler, IntPtr inContext);
        [DllImport("EDSDK.dll")] public static extern uint EdsSetCameraStateEventHandler(IntPtr inCameraRef, uint inEvent, StateEventHandler inHandler, IntPtr inContext);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetDirectoryItemInfo(IntPtr inDirItemRef, out DirectoryItemInfo outDirItemInfo);
        [DllImport("EDSDK.dll")] public static extern uint EdsDownload(IntPtr inDirItemRef, ulong inReadSize, IntPtr outStream);
        [DllImport("EDSDK.dll")] public static extern uint EdsDownloadComplete(IntPtr inDirItemRef);
        [DllImport("EDSDK.dll")] public static extern uint EdsDownloadCancel(IntPtr inDirItemRef);
        [DllImport("EDSDK.dll")] public static extern uint EdsCreateMemoryStream(ulong inBufferSize, out IntPtr outStream);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetPointer(IntPtr inStream, out IntPtr outPointer);
        [DllImport("EDSDK.dll")] public static extern uint EdsGetLength(IntPtr inStream, out ulong outLength);
        [DllImport("EDSDK.dll")] public static extern uint EdsCreateEvfImageRef(IntPtr inStreamRef, out IntPtr outEvfImageRef);
        [DllImport("EDSDK.dll")] public static extern uint EdsDownloadEvfImage(IntPtr inCameraRef, IntPtr inEvfImageRef);
    }

    internal sealed class HelperException : Exception
    {
        public readonly string Code;
        public readonly uint EdsError;
        public HelperException(string code, uint edsError, string message) : base(message)
        {
            Code = code;
            EdsError = edsError;
        }
    }

    internal static class Program
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly Queue<string> Pending = new Queue<string>();
        private static readonly object OutLock = new object();
        private static bool stdinClosed;

        // Giữ tham chiếu delegate — GC thu mất thì EDSDK gọi vào vùng nhớ đã giải phóng.
        private static Eds.ObjectEventHandler objectHandler;
        private static Eds.StateEventHandler stateHandler;

        private static IntPtr camera = IntPtr.Zero;
        private static bool sessionOpen;
        private static IntPtr pendingTransfer = IntPtr.Zero;
        private static bool liveViewOn;

        [STAThread]
        private static int Main(string[] args)
        {
            Console.InputEncoding = Encoding.UTF8;
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            stdout.AutoFlush = true;
            Console.SetOut(stdout);

            uint err = Eds.EdsInitializeSDK();
            if (err != Eds.OK)
            {
                Emit(new Dictionary<string, object> { { "event", "fatal" }, { "message", "EdsInitializeSDK lỗi " + Hex(err) } });
                return 2;
            }
            objectHandler = OnObjectEvent;
            stateHandler = OnStateEvent;

            var reader = new Thread(ReadStdin);
            reader.IsBackground = true;
            reader.Start();
            Emit(new Dictionary<string, object> { { "event", "ready" } });

            while (true)
            {
                Eds.EdsGetEvent();
                string line = null;
                lock (Pending)
                {
                    if (Pending.Count > 0) line = Pending.Dequeue();
                    else if (stdinClosed) break;
                }
                if (line == null)
                {
                    Thread.Sleep(5);
                    continue;
                }
                Handle(line);
            }

            CloseSession();
            Eds.EdsTerminateSDK();
            return 0;
        }

        private static void ReadStdin()
        {
            string line;
            while ((line = Console.In.ReadLine()) != null)
            {
                if (line.Trim().Length == 0) continue;
                lock (Pending) Pending.Enqueue(line);
            }
            lock (Pending) stdinClosed = true;
        }

        private static void Emit(Dictionary<string, object> message)
        {
            string text = Json.Serialize(message);
            lock (OutLock) Console.Out.WriteLine(text);
        }

        private static string Hex(uint value)
        {
            return "0x" + value.ToString("X");
        }

        private static void Handle(string line)
        {
            object id = null;
            try
            {
                var request = Json.Deserialize<Dictionary<string, object>>(line);
                id = request.ContainsKey("id") ? request["id"] : null;
                string cmd = request.ContainsKey("cmd") ? Convert.ToString(request["cmd"]) : "";
                object result = Dispatch(cmd, request);
                Emit(new Dictionary<string, object> { { "id", id }, { "ok", true }, { "result", result } });
            }
            catch (HelperException ex)
            {
                if (ex.Code == "DISCONNECTED") DropSession();
                Emit(new Dictionary<string, object> {
                    { "id", id }, { "ok", false }, { "code", ex.Code }, { "eds", (long)ex.EdsError }, { "message", ex.Message } });
            }
            catch (Exception ex)
            {
                Emit(new Dictionary<string, object> {
                    { "id", id }, { "ok", false }, { "code", "INTERNAL" }, { "eds", 0L }, { "message", ex.Message } });
            }
        }

        private static object Dispatch(string cmd, Dictionary<string, object> request)
        {
            switch (cmd)
            {
                case "status": return Status();
                case "liveViewStart": return LiveViewStart();
                case "liveViewFrame": return LiveViewFrame();
                case "liveViewStop": return LiveViewStop();
                case "capture": return Capture(Str(request, "path"), Bool(request, "autofocus", true), Int(request, "timeoutMs", 20000));
                case "get": return GetProperty((uint)Int(request, "prop", 0));
                case "set": return SetProperty((uint)Int(request, "prop", 0), (uint)Long(request, "value"));
                case "driveLens": return DriveLens(Int(request, "step", 0));
                case "importRecent": return ImportRecent(Str(request, "dir"), Int(request, "count", 1));
                case "close": CloseSession(); return true;
                default: throw new HelperException("BAD_COMMAND", 0, "Lệnh không hợp lệ: " + cmd);
            }
        }

        private static string Str(Dictionary<string, object> r, string key)
        {
            if (!r.ContainsKey(key) || r[key] == null) throw new HelperException("BAD_COMMAND", 0, "Thiếu " + key);
            return Convert.ToString(r[key]);
        }

        private static bool Bool(Dictionary<string, object> r, string key, bool fallback)
        {
            return r.ContainsKey(key) && r[key] != null ? Convert.ToBoolean(r[key]) : fallback;
        }

        private static int Int(Dictionary<string, object> r, string key, int fallback)
        {
            return r.ContainsKey(key) && r[key] != null ? Convert.ToInt32(r[key]) : fallback;
        }

        private static long Long(Dictionary<string, object> r, string key)
        {
            if (!r.ContainsKey(key) || r[key] == null) throw new HelperException("BAD_COMMAND", 0, "Thiếu " + key);
            return Convert.ToInt64(r[key]);
        }

        // ── Phiên làm việc với máy ─────────────────────────────────────────────────

        private static bool IsDisconnectError(uint err)
        {
            // DEVICE_NOT_FOUND 0x80, COMM_* 0xC0–0xC3, SESSION_NOT_OPEN 0x2003, INVALID_HANDLE 0x61
            return err == 0x80 || (err >= 0xC0 && err <= 0xC3) || err == 0x2003 || err == 0x61;
        }

        private static void Check(uint err, string what)
        {
            if (err == Eds.OK) return;
            if (IsDisconnectError(err)) throw new HelperException("DISCONNECTED", err, "Mất kết nối máy ảnh (" + what + ", " + Hex(err) + ")");
            if (err == Eds.ERR_DEVICE_BUSY) throw new HelperException("BUSY", err, "Máy ảnh đang bận (" + what + ")");
            throw new HelperException("EDS_ERROR", err, what + " lỗi " + Hex(err));
        }

        private static bool EnsureSession()
        {
            if (sessionOpen) return true;
            IntPtr list;
            Check(Eds.EdsGetCameraList(out list), "GetCameraList");
            try
            {
                int count;
                Check(Eds.EdsGetChildCount(list, out count), "GetChildCount");
                if (count == 0) return false;
                IntPtr cam;
                Check(Eds.EdsGetChildAtIndex(list, 0, out cam), "GetChildAtIndex");
                uint err = Eds.EdsOpenSession(cam);
                if (err != Eds.OK)
                {
                    Eds.EdsRelease(cam);
                    Check(err, "OpenSession");
                }
                camera = cam;
                sessionOpen = true;
                Eds.EdsSetObjectEventHandler(camera, Eds.ObjectEvent_All, objectHandler, IntPtr.Zero);
                Eds.EdsSetCameraStateEventHandler(camera, Eds.StateEvent_All, stateHandler, IntPtr.Zero);
                // Lưu cả thẻ nhớ lẫn máy tính (booth giữ bản gốc trên thẻ). Báo dung lượng "còn nhiều"
                // để máy không chặn chụp vì tưởng máy tính hết chỗ.
                uint saveTo = Eds.SaveTo_Both;
                Eds.EdsSetPropertyData(camera, Eds.PropID_SaveTo, 0, 4, ref saveTo);
                var capacity = new Eds.Capacity { NumberOfFreeClusters = 0x7FFFFFFF, BytesPerSector = 0x1000, Reset = 1 };
                Eds.EdsSetCapacity(camera, capacity);
                // Yêu cầu chuyển ảnh còn treo từ phiên trước (app bị tắt giữa lúc tải) tới ngay khi mở
                // phiên — nhận rồi huỷ, không thì lần bấm đầu tiên đã BUSY.
                var flushUntil = DateTime.UtcNow.AddMilliseconds(400);
                while (DateTime.UtcNow < flushUntil)
                {
                    Eds.EdsGetEvent();
                    Thread.Sleep(10);
                }
                CancelPendingTransfer();
                return true;
            }
            finally
            {
                Eds.EdsRelease(list);
            }
        }

        private static void RequireSession()
        {
            if (!EnsureSession()) throw new HelperException("NO_CAMERA", 0, "Chưa thấy máy ảnh Canon nào cắm vào máy tính");
        }

        // Máy chờ ảnh "chuyển về máy tính" tới khi host tải xong HOẶC huỷ. Chỉ EdsRelease thì máy
        // vẫn treo yêu cầu đó: chế độ lưu cả thẻ lẫn máy tính bấm nút nào cũng trả BUSY (0x81)/0x8D07,
        // tắt/bật nguồn không gỡ (đo trên R50 desktop-6rr8i2o 19/10/2026 sau khi app bị tắt giữa lúc tải).
        private static void CancelPendingTransfer()
        {
            if (pendingTransfer == IntPtr.Zero) return;
            if (sessionOpen) Eds.EdsDownloadCancel(pendingTransfer);
            Eds.EdsRelease(pendingTransfer);
            pendingTransfer = IntPtr.Zero;
        }

        private static void DropSession()
        {
            CancelPendingTransfer();
            if (camera != IntPtr.Zero) Eds.EdsRelease(camera);
            camera = IntPtr.Zero;
            sessionOpen = false;
            liveViewOn = false;
        }

        private static void CloseSession()
        {
            if (sessionOpen && camera != IntPtr.Zero)
            {
                if (liveViewOn) StopEvfOutput();
                Eds.EdsCloseSession(camera);
            }
            DropSession();
        }

        private static uint OnObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            if (inEvent == Eds.ObjectEvent_DirItemRequestTransfer)
            {
                CancelPendingTransfer();
                pendingTransfer = inRef; // giữ tham chiếu tới lúc tải xong
                return Eds.OK;
            }
            if (inRef != IntPtr.Zero) Eds.EdsRelease(inRef);
            return Eds.OK;
        }

        private static uint OnStateEvent(uint inEvent, uint inParameter, IntPtr inContext)
        {
            if (inEvent == Eds.StateEvent_Shutdown)
            {
                DropSession();
                Emit(new Dictionary<string, object> { { "event", "disconnected" } });
            }
            return Eds.OK;
        }

        // ── Lệnh ───────────────────────────────────────────────────────────────────

        private static object Status()
        {
            if (!EnsureSession()) return new Dictionary<string, object> { { "connected", false } };
            var result = new Dictionary<string, object> { { "connected", true } };
            result["model"] = GetString(Eds.PropID_ProductName);
            result["serial"] = GetString(Eds.PropID_BodyIDEx);
            uint battery;
            if (Eds.EdsGetPropertyData(camera, Eds.PropID_BatteryLevel, 0, 4, out battery) == Eds.OK) result["battery"] = (long)battery;
            return result;
        }

        private static string GetString(uint prop)
        {
            uint type;
            int size;
            if (Eds.EdsGetPropertySize(camera, prop, 0, out type, out size) != Eds.OK || size <= 0) return "";
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (Eds.EdsGetPropertyData(camera, prop, 0, size, buffer) != Eds.OK) return "";
                return Marshal.PtrToStringAnsi(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static object LiveViewStart()
        {
            RequireSession();
            uint device;
            Check(Eds.EdsGetPropertyData(camera, Eds.PropID_Evf_OutputDevice, 0, 4, out device), "Get Evf_OutputDevice");
            uint wanted = device | Eds.EvfOutputDevice_PC;
            if (wanted != device) Check(Eds.EdsSetPropertyData(camera, Eds.PropID_Evf_OutputDevice, 0, 4, ref wanted), "Set Evf_OutputDevice");
            liveViewOn = true;
            return true;
        }

        private static void StopEvfOutput()
        {
            uint device;
            if (Eds.EdsGetPropertyData(camera, Eds.PropID_Evf_OutputDevice, 0, 4, out device) != Eds.OK) return;
            uint wanted = (device & ~Eds.EvfOutputDevice_PC) | Eds.EvfOutputDevice_TFT;
            if (wanted != device) Eds.EdsSetPropertyData(camera, Eds.PropID_Evf_OutputDevice, 0, 4, ref wanted);
        }

        private static object LiveViewStop()
        {
            if (sessionOpen && liveViewOn) StopEvfOutput();
            liveViewOn = false;
            return true;
        }

        private static object LiveViewFrame()
        {
            RequireSession();
            IntPtr stream = IntPtr.Zero;
            IntPtr evf = IntPtr.Zero;
            try
            {
                Check(Eds.EdsCreateMemoryStream(0, out stream), "CreateMemoryStream");
                Check(Eds.EdsCreateEvfImageRef(stream, out evf), "CreateEvfImageRef");
                uint err = Eds.EdsDownloadEvfImage(camera, evf);
                if (err == Eds.ERR_OBJECT_NOTREADY) return new Dictionary<string, object> { { "notReady", true } };
                Check(err, "DownloadEvfImage");
                // Thu nho live view de giam lag
                using (var srcImg = new System.Drawing.Bitmap(new System.IO.MemoryStream(StreamBytes(stream)))) {
                    int nw = 640, nh = 427;
                    using (var bmp = new System.Drawing.Bitmap(nw, nh)) {
                        using (var g = System.Drawing.Graphics.FromImage(bmp)) {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(srcImg, 0, 0, nw, nh);
                        }
                        var enc = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()[1];
                        var ep = new System.Drawing.Imaging.EncoderParameters(1);
                        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
                        var ms = new System.IO.MemoryStream();
                        bmp.Save(ms, enc, ep);
                        return new Dictionary<string, object> { { "jpeg", Convert.ToBase64String(ms.ToArray()) } };
                    }
                }
            }
            finally
            {
                if (evf != IntPtr.Zero) Eds.EdsRelease(evf);
                if (stream != IntPtr.Zero) Eds.EdsRelease(stream);
            }
        }

        private static byte[] StreamBytes(IntPtr stream)
        {
            IntPtr pointer;
            ulong length;
            Check(Eds.EdsGetPointer(stream, out pointer), "GetPointer");
            Check(Eds.EdsGetLength(stream, out length), "GetLength");
            var bytes = new byte[(int)length];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }

        /// Bấm hết nút (có hoặc không lấy nét) rồi LUÔN nhả nút, kể cả khi trượt nét — đây là
        /// điều digiCamControl bỏ sót. Sau đó chờ máy đẩy ảnh sang và tải về `path`.
        private static object Capture(string path, bool autofocus, int timeoutMs)
        {
            RequireSession();
            CancelPendingTransfer();
            int button = autofocus ? Eds.ShutterButton_Completely : Eds.ShutterButton_Completely_NonAF;
            uint pressErr;
            uint releaseErr;
            try
            {
                pressErr = Eds.EdsSendCommand(camera, Eds.CameraCommand_PressShutterButton, button);
            }
            finally
            {
                releaseErr = Eds.EdsSendCommand(camera, Eds.CameraCommand_PressShutterButton, Eds.ShutterButton_OFF);
            }
            if (pressErr == Eds.ERR_TAKE_PICTURE_AF_NG)
                throw new HelperException("AF_FAILED", pressErr, "Máy ảnh không bắt được nét (đã nhả nút)");
            Check(pressErr, "PressShutterButton");
            if (releaseErr != Eds.OK && IsDisconnectError(releaseErr)) Check(releaseErr, "ReleaseShutterButton");

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (pendingTransfer == IntPtr.Zero && DateTime.UtcNow < deadline)
            {
                Eds.EdsGetEvent();
                Thread.Sleep(5);
                if (!sessionOpen) throw new HelperException("DISCONNECTED", 0, "Máy ảnh ngắt kết nối khi đang chụp");
            }
            if (pendingTransfer == IntPtr.Zero)
                throw new HelperException("NO_TRANSFER", 0, "Máy ảnh đã chụp nhưng ảnh không chuyển về máy tính");

            IntPtr item = pendingTransfer;
            pendingTransfer = IntPtr.Zero;
            try
            {
                return DownloadItem(item, path);
            }
            finally
            {
                Eds.EdsRelease(item);
            }
        }

        private static Dictionary<string, object> DownloadItem(IntPtr item, string path)
        {
            Eds.DirectoryItemInfo info;
            Check(Eds.EdsGetDirectoryItemInfo(item, out info), "GetDirectoryItemInfo");
            IntPtr stream;
            Check(Eds.EdsCreateMemoryStream(0, out stream), "CreateMemoryStream");
            try
            {
                uint err = Eds.EdsDownload(item, info.Size, stream);
                if (err != Eds.OK)
                {
                    Eds.EdsDownloadCancel(item);
                    Check(err, "Download");
                }
                Check(Eds.EdsDownloadComplete(item), "DownloadComplete");
                byte[] bytes = StreamBytes(stream);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, bytes);
                return new Dictionary<string, object> { { "path", path }, { "name", info.szFileName }, { "size", (long)bytes.Length } };
            }
            finally
            {
                Eds.EdsRelease(stream);
            }
        }

        private static object GetProperty(uint prop)
        {
            RequireSession();
            uint value;
            Check(Eds.EdsGetPropertyData(camera, prop, 0, 4, out value), "Get " + Hex(prop));
            var choices = new List<object>();
            Eds.PropertyDesc desc;
            if (Eds.EdsGetPropertyDesc(camera, prop, out desc) == Eds.OK && desc.PropDesc != null)
            {
                for (int i = 0; i < desc.NumElements && i < desc.PropDesc.Length; i++) choices.Add((long)(uint)desc.PropDesc[i]);
            }
            return new Dictionary<string, object> { { "value", (long)value }, { "choices", choices } };
        }

        private static object SetProperty(uint prop, uint value)
        {
            RequireSession();
            uint data = value;
            uint err = Eds.EdsSetPropertyData(camera, prop, 0, 4, ref data);
            // Máy vừa bấm/đổi xong hay báo bận một nhịp — thử lại vài lần trước khi bỏ cuộc.
            for (int attempt = 0; err == Eds.ERR_DEVICE_BUSY && attempt < 5; attempt++)
            {
                Thread.Sleep(200);
                Eds.EdsGetEvent();
                err = Eds.EdsSetPropertyData(camera, prop, 0, 4, ref data);
            }
            Check(err, "Set " + Hex(prop));
            return GetProperty(prop);
        }

        private static object DriveLens(int step)
        {
            RequireSession();
            // Near3..Near1 = 0x0003..0x0001, Far1..Far3 = 0x8001..0x8003
            int param = step < 0 ? Math.Min(3, -step) : 0x8000 | Math.Min(3, step);
            if (step == 0) return true;
            Check(Eds.EdsSendCommand(camera, Eds.CameraCommand_DriveLensEvf, param), "DriveLensEvf");
            return true;
        }

        /// Chép `count` ảnh JPEG mới nhất trên thẻ (thư mục DCIM cuối cùng) về `dir`.
        private static object ImportRecent(string dir, int count)
        {
            RequireSession();
            IntPtr volume = ChildAt(camera, 0);
            try
            {
                IntPtr dcim = FindChild(volume, "DCIM", true);
                if (dcim == IntPtr.Zero) throw new HelperException("NO_PHOTOS", 0, "Thẻ nhớ chưa có thư mục DCIM");
                try
                {
                    IntPtr folder = LastChild(dcim, true, null);
                    if (folder == IntPtr.Zero) throw new HelperException("NO_PHOTOS", 0, "Thẻ nhớ chưa có ảnh");
                    try
                    {
                        var items = new List<KeyValuePair<string, IntPtr>>();
                        int n;
                        Check(Eds.EdsGetChildCount(folder, out n), "GetChildCount");
                        for (int i = 0; i < n; i++)
                        {
                            IntPtr child = ChildAt(folder, i);
                            Eds.DirectoryItemInfo info;
                            if (Eds.EdsGetDirectoryItemInfo(child, out info) == Eds.OK && info.IsFolder == 0 &&
                                info.szFileName.EndsWith(".JPG", StringComparison.OrdinalIgnoreCase))
                                items.Add(new KeyValuePair<string, IntPtr>(info.szFileName, child));
                            else
                                Eds.EdsRelease(child);
                        }
                        items.Sort(delegate (KeyValuePair<string, IntPtr> a, KeyValuePair<string, IntPtr> b) { return string.CompareOrdinal(a.Key, b.Key); });
                        var result = new List<object>();
                        for (int i = 0; i < items.Count; i++)
                        {
                            if (i >= items.Count - count)
                                result.Add(DownloadItem(items[i].Value, Path.Combine(dir, items[i].Key)));
                            Eds.EdsRelease(items[i].Value);
                        }
                        return result;
                    }
                    finally { Eds.EdsRelease(folder); }
                }
                finally { Eds.EdsRelease(dcim); }
            }
            finally { Eds.EdsRelease(volume); }
        }

        private static IntPtr ChildAt(IntPtr parent, int index)
        {
            IntPtr child;
            Check(Eds.EdsGetChildAtIndex(parent, index, out child), "GetChildAtIndex");
            return child;
        }

        private static IntPtr FindChild(IntPtr parent, string name, bool folder)
        {
            int n;
            Check(Eds.EdsGetChildCount(parent, out n), "GetChildCount");
            for (int i = 0; i < n; i++)
            {
                IntPtr child = ChildAt(parent, i);
                Eds.DirectoryItemInfo info;
                if (Eds.EdsGetDirectoryItemInfo(child, out info) == Eds.OK && (info.IsFolder != 0) == folder &&
                    string.Equals(info.szFileName, name, StringComparison.OrdinalIgnoreCase))
                    return child;
                Eds.EdsRelease(child);
            }
            return IntPtr.Zero;
        }

        private static IntPtr LastChild(IntPtr parent, bool folder, string suffix)
        {
            int n;
            Check(Eds.EdsGetChildCount(parent, out n), "GetChildCount");
            IntPtr best = IntPtr.Zero;
            string bestName = null;
            for (int i = 0; i < n; i++)
            {
                IntPtr child = ChildAt(parent, i);
                Eds.DirectoryItemInfo info;
                bool keep = Eds.EdsGetDirectoryItemInfo(child, out info) == Eds.OK && (info.IsFolder != 0) == folder &&
                    (suffix == null || info.szFileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) &&
                    (bestName == null || string.CompareOrdinal(info.szFileName, bestName) > 0);
                if (keep)
                {
                    if (best != IntPtr.Zero) Eds.EdsRelease(best);
                    best = child;
                    bestName = info.szFileName;
                }
                else Eds.EdsRelease(child);
            }
            return best;
        }
    }
}
