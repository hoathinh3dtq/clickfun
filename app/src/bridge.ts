// Minimal typed client for the ClickFun.CameraBridge IPC (HTTP + token).
// Bridge defaults to http://127.0.0.1:8737; override via the settings panel.

export interface CameraStatus {
  connected: boolean;
  cameraModel: string;
  battery: string | null;
  freeBytesOnCard: number | null;
  backend: string;
}

export interface SettingsDto {
  printerName: string;
  copies: number;
  zoom: number;
  offsetX: number;
  offsetY: number;
  countdownSeconds: number;
  defaultFilter: string;
  overlayTemplate: string;
  photosRoot: string;
  videoMaxSeconds: number;
  paperLayout: string;
}

export interface CaptureResult {
  success: boolean;
  filePath?: string;
  error?: string;
}

const DEFAULT_BASE = "http://127.0.0.1:8737";

export class BridgeClient {
  constructor(
    private baseUrl: string = DEFAULT_BASE,
    private token: string = "clickfun-dev-token"
  ) {}

  static fromLocalStorage(): BridgeClient {
    return new BridgeClient(
      localStorage.getItem("clickfun.bridgeUrl") ?? DEFAULT_BASE,
      localStorage.getItem("clickfun.bridgeToken") ?? "clickfun-dev-token"
    );
  }

  async health() {
    const res = await fetch(`${this.baseUrl}/health`);
    if (!res.ok) throw new Error(`health failed: ${res.status}`);
    return (await res.json()) as { status: string; version: string; cameraBackend: string; printerAvailable: boolean };
  }

  private headers(): HeadersInit {
    return { "X-ClickFun-Token": this.token };
  }

  async status(): Promise<CameraStatus> {
    const res = await fetch(`${this.baseUrl}/camera/status`, { headers: this.headers() });
    return res.json();
  }

  async connect(): Promise<CameraStatus> {
    const res = await fetch(`${this.baseUrl}/camera/connect`, { method: "POST", headers: this.headers() });
    if (!res.ok) throw new Error(`connect failed: ${res.status}`);
    return res.json();
  }

  async capture(): Promise<CaptureResult> {
    const res = await fetch(`${this.baseUrl}/capture`, { method: "POST", headers: this.headers() });
    if (!res.ok) return { success: false, error: `capture failed: ${res.status}` };
    return res.json();
  }

  frameStreamUrl(): string {
    return `${this.baseUrl}/stream.mjpg?token=${encodeURIComponent(this.token)}`;
  }

  async getSettings(): Promise<SettingsDto> {
    const res = await fetch(`${this.baseUrl}/settings`, { headers: this.headers() });
    return res.json();
  }

  async saveSettings(dto: SettingsDto): Promise<SettingsDto> {
    const res = await fetch(`${this.baseUrl}/settings`, {
      method: "PUT",
      headers: { ...this.headers(), "Content-Type": "application/json" },
      body: JSON.stringify(dto),
    });
    return res.json();
  }

  async print(imagePath: string, copies?: number, paperLayout?: string): Promise<{ success: boolean; message: string; paperLayout?: string }> {
    const res = await fetch(`${this.baseUrl}/print`, {
      method: "POST",
      headers: { ...this.headers(), "Content-Type": "application/json" },
      body: JSON.stringify({ imagePath, copies, paperLayout }),
    });
    if (!res.ok) return { success: false, message: `print failed: ${res.status}` };
    return res.json();
  }
}
