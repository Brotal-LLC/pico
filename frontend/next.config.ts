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
};

export default nextConfig;