"use client";

import { useEffect, useRef, useState, useCallback } from "react";
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import "@xterm/xterm/css/xterm.css";
import { resources } from "@/lib/api";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle, CardDescription } from "@/components/ui/Card";
import { Terminal as TerminalIcon, Wifi, WifiOff } from "lucide-react";

interface WSWithDisposable extends WebSocket {
  __inputDisposable?: { dispose: () => void };
}

interface VmShellPanelProps {
  resourceId: string;
  /** Only render when the VM is in Running state */
  isRunning: boolean;
}

type ConnectionState = "disconnected" | "connecting" | "connected" | "error";

export function VmShellPanel({ resourceId, isRunning }: VmShellPanelProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const termRef = useRef<Terminal | null>(null);
  const fitRef = useRef<FitAddon | null>(null);
  const wsRef = useRef<WebSocket | null>(null);
  const [state, setState] = useState<ConnectionState>("disconnected");
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  // Initialize terminal once
  useEffect(() => {
    if (!containerRef.current || termRef.current) return;

    const term = new Terminal({
      fontSize: 13,
      fontFamily: "'JetBrains Mono', 'Fira Code', monospace",
      cursorBlink: true,
      disableStdin: false,
      scrollback: 1000,
      theme: {
        background: "#0a0a0a",
        foreground: "#e0e0e0",
        cursor: "#e0e0e0",
        selectionBackground: "#444444",
      },
    });

    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(containerRef.current);
    fit.fit();

    termRef.current = term;
    fitRef.current = fit;

    // Handle resize
    const handleResize = () => fit.fit();
    window.addEventListener("resize", handleResize);

    return () => {
      window.removeEventListener("resize", handleResize);
      term.dispose();
      termRef.current = null;
    };
  }, []);

  const connect = useCallback(() => {
    if (!termRef.current || wsRef.current) return;

    setState("connecting");
    setErrorMsg(null);
    termRef.current.writeln("\r\n\x1b[33mConnecting to shell...\x1b[0m\r\n");

    const url = resources.shellUrl(resourceId);
    const ws = new WebSocket(url);
    wsRef.current = ws;

    ws.onopen = () => {
      setState("connected");
      termRef.current?.writeln("\x1b[32m✓ Connected\x1b[0m\r\n");
      termRef.current?.focus();
    };

    ws.onmessage = (e) => {
      // e.data is a string (our endpoint sends Text messages)
      if (typeof e.data === "string") {
        termRef.current?.write(e.data);
      } else if (e.data instanceof Blob) {
        e.data.text().then((text) => termRef.current?.write(text));
      }
    };

    ws.onerror = () => {
      setState("error");
      setErrorMsg("Connection error — the VM may have stopped or the session timed out.");
      termRef.current?.writeln("\r\n\x1b[31m✗ Connection error\x1b[0m\r\n");
    };

    ws.onclose = (e) => {
      if (state !== "error") {
        setState("disconnected");
        termRef.current?.writeln(`\r\n\x1b[33m[Session ended${e.reason ? ": " + e.reason : ""}]\x1b[0m\r\n`);
      }
      wsRef.current = null;
    };

    // Pipe terminal input → WebSocket
    const inputDisposable = termRef.current.onData((data) => {
      if (ws.readyState === WebSocket.OPEN) {
        ws.send(data);
      }
    });

    // Store disposable for cleanup
    const wsWithDisp = wsRef.current as WSWithDisposable | null;
    if (wsWithDisp) wsWithDisp.__inputDisposable = inputDisposable;
  }, [resourceId, state]);

  const disconnect = useCallback(() => {
    if (wsRef.current) {
      const disp = (wsRef.current as WSWithDisposable | null)?.__inputDisposable;
      if (disp) disp.dispose();
      wsRef.current.close(1000, "Client closed");
      wsRef.current = null;
    }
    setState("disconnected");
  }, []);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (wsRef.current) {
        const disp = (wsRef.current as WSWithDisposable | null)?.__inputDisposable;
        if (disp) disp.dispose();
        wsRef.current.close();
        wsRef.current = null;
      }
    };
  }, []);

  if (!isRunning) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <TerminalIcon className="h-4 w-4" />
            Shell Access
          </CardTitle>
          <CardDescription className="mt-1">
            The VM must be running to open a shell session.
          </CardDescription>
        </CardHeader>
        <CardBody>
          <p className="text-sm text-muted-foreground">
            Start the VM to enable interactive shell access.
          </p>
        </CardBody>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <TerminalIcon className="h-4 w-4" />
              Shell Access
            </CardTitle>
            <CardDescription className="mt-1">
              Interactive terminal session over WebSocket
            </CardDescription>
          </div>
          <div className="flex items-center gap-2">
            {state === "connected" ? (
              <span className="flex items-center gap-1.5 text-xs text-success">
                <Wifi className="h-3 w-3" />
                Connected
              </span>
            ) : state === "connecting" ? (
              <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <Wifi className="h-3 w-3 animate-pulse" />
                Connecting…
              </span>
            ) : (
              <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <WifiOff className="h-3 w-3" />
                Disconnected
              </span>
            )}
            {state === "connected" ? (
              <Button variant="outline" onClick={disconnect} className="text-xs">
                Disconnect
              </Button>
            ) : (
              <Button
                variant="outline"
                onClick={connect}
                disabled={state === "connecting"}
                className="text-xs"
              >
                Connect
              </Button>
            )}
          </div>
        </div>
      </CardHeader>
      <CardBody>
        {errorMsg && (
          <p className="text-sm text-error mb-3">{errorMsg}</p>
        )}
        <div
          ref={containerRef}
          className="h-80 w-full overflow-hidden rounded border border-border bg-[#0a0a0a]"
          style={{ padding: "8px" }}
        />
      </CardBody>
    </Card>
  );
}