import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  // Output standalone for Docker — smaller image, faster startup
  output: "standalone",
  // Allow API URL to be set at build time for the Docker container
  env: {
    NEXT_PUBLIC_API_URL: process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080",
    API_URL: process.env.API_URL ?? "http://api:8080",
  },
  experimental: {
    // Use SWC for faster builds (Webpack mode)
    optimizePackageImports: ["lucide-react", "recharts"],
  },
  // Hide X-Powered-By: Next.js so the framework is not fingerprintable.
  poweredByHeader: false,
  // Security response headers (next-server emits them on every response).
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
          {
            key: "Permissions-Policy",
            value: "camera=(), microphone=(), geolocation=()",
          },
          {
            key: "Strict-Transport-Security",
            value: "max-age=63072000; includeSubDomains; preload",
          },
        ],
      },
    ];
  },
};

export default nextConfig;