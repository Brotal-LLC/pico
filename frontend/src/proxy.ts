import { NextResponse, type NextRequest } from "next/server";

const SENSITIVE_LOGIN_PARAMS = ["email", "password"];

export function proxy(request: NextRequest) {
  const url = request.nextUrl;

  if (url.pathname === "/login" && SENSITIVE_LOGIN_PARAMS.some((key) => url.searchParams.has(key))) {
    const cleanUrl = url.clone();
    SENSITIVE_LOGIN_PARAMS.forEach((key) => cleanUrl.searchParams.delete(key));
    cleanUrl.search = "";
    return NextResponse.redirect(cleanUrl);
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/login"],
};
