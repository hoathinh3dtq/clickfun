import { useCallback, useEffect, useRef, useState } from "react";
import { BridgeClient, type SettingsDto, type CaptureResult } from "./bridge";

type Screen = "home" | "countdown" | "preview" | "done";

export default function App() {
  const [screen, setScreen] = useState<Screen>("home");
  const [countdown, setCountdown] = useState(3);
  const [capturedPath, setCapturedPath] = useState<string | null>(null);
  const [frameUrl, setFrameUrl] = useState<string | null>(null);
  const [settings, setSettings] = useState<SettingsDto | null>(null);
  const [showSettings, setShowSettings] = useState(false);
  const [status, setStatus] = useState("connecting…");
  const [printing, setPrinting] = useState(false);
  const bridgeRef = useRef(BridgeClient.fromLocalStorage());
  const objectUrlRef = useRef<string | null>(null);

  const refreshFrame = useCallback(async () => {
    try {
      const blob = await bridgeRef.current.fetchFrameBlob();
      if (blob) {
        if (objectUrlRef.current) URL.revokeObjectURL(objectUrlRef.current);
        objectUrlRef.current = URL.createObjectURL(blob);
        setFrameUrl(objectUrlRef.current);
        setStatus("live");
      } else {
        setStatus("camera idle");
      }
    } catch {
      setStatus("bridge unreachable");
    }
  }, []);

  useEffect(() => {
    const id = setInterval(refreshFrame, 250);
    return () => clearInterval(id);
  }, [refreshFrame]);

  useEffect(() => {
    bridgeRef.current.getSettings().then(setSettings).catch(() => setSettings(null));
  }, []);

  async function startCapture() {
    setScreen("countdown");
    const seconds = settings?.countdownSeconds ?? 3;
    for (let i = seconds; i > 0; i--) {
      setCountdown(i);
      await new Promise((r) => setTimeout(r, 1000));
    }
    const result: CaptureResult = await bridgeRef.current.capture();
    if (result.success && result.filePath) {
      setCapturedPath(result.filePath);
      setScreen("preview");
    } else {
      setStatus(result.error ?? "capture failed");
      setScreen("home");
    }
  }

  async function doPrint() {
    if (!capturedPath) return;
    setPrinting(true);
    const result = await bridgeRef.current.print(capturedPath, settings?.copies ?? 1);
    setPrinting(false);
    setStatus(result.message);
    setScreen("done");
  }

  return (
    <div className="screen">
      <header className="brand" onDoubleClick={() => setShowSettings(true)}>
        Click<span>Fun</span>
      </header>

      {frameUrl && screen !== "preview" && <img className="viewfinder" src={frameUrl} alt="" />}

      {screen === "home" && (
        <button className="shutter" onClick={startCapture}>📸 Chụp ngay</button>
      )}

      {screen === "countdown" && <div className="countdown">{countdown}</div>}

      {screen === "preview" && (
        <div className="preview-bar">
          <button onClick={() => setScreen("home")}>↺ Chụp lại</button>
          <button className="primary" disabled={printing} onClick={doPrint}>
            {printing ? "Đang in…" : "🖨 In ảnh"}
          </button>
        </div>
      )}

      {screen === "done" && (
        <div className="preview-bar">
          <p>{status}</p>
          <button className="primary" onClick={() => setScreen("home")}>Xong</button>
        </div>
      )}

      <footer className="status">{status}</footer>

      {showSettings && (
        <div className="settings-modal" onClick={() => setShowSettings(false)}>
          <div className="settings-card" onClick={(e) => e.stopPropagation()}>
            <h2>Cài đặt</h2>
            <p>Bridge: {localStorage.getItem("clickfun.bridgeUrl") ?? "http://127.0.0.1:8737"}</p>
            <p>Printer: {settings?.printerName || "(mặc định)"} · Copies: {settings?.copies} · Zoom: {settings?.zoom}</p>
            <p>Countdown: {settings?.countdownSeconds}s · Filter: {settings?.defaultFilter}</p>
            <button onClick={() => setShowSettings(false)}>Đóng</button>
          </div>
        </div>
      )}
    </div>
  );
}
