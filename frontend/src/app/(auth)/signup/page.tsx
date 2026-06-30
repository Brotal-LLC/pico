"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useAuth } from "@/components/AuthProvider";
import { Button } from "@/components/ui/Button";
import { Input, Label } from "@/components/ui/Input";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";

const schema = z.object({
  name: z.string().min(1, "Name required"),
  email: z.string().email("Invalid email"),
  password: z.string().min(6, "Min 6 characters"),
});

type SignupForm = z.infer<typeof schema>;

export default function SignupPage() {
  const { signup } = useAuth();
  const router = useRouter();
  const [submitError, setSubmitError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<SignupForm>({ resolver: zodResolver(schema) });

  const onSubmit = handleSubmit(async (data) => {
    setSubmitError(null);
    try {
      await signup(data.email, data.name, data.password);
      router.replace("/dashboard");
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : "Sign up failed");
    }
  });

  return (
    <main className="min-h-screen flex items-center justify-center bg-background px-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>Create your account</CardTitle>
        </CardHeader>
        <CardBody>
          <form onSubmit={onSubmit} className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="name">Full name</Label>
              <Input id="name" autoComplete="name" {...register("name")} />
              {errors.name && <p className="text-xs text-error">{errors.name.message}</p>}
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="email">Email</Label>
              <Input id="email" type="email" autoComplete="email" {...register("email")} />
              {errors.email && <p className="text-xs text-error">{errors.email.message}</p>}
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="password">Password</Label>
              <Input id="password" type="password" autoComplete="new-password" {...register("password")} />
              {errors.password && <p className="text-xs text-error">{errors.password.message}</p>}
            </div>
            {submitError && <p className="text-sm text-error">{submitError}</p>}
            <Button type="submit" className="w-full" disabled={isSubmitting}>
              {isSubmitting ? "Creating account…" : "Create account"}
            </Button>
            <p className="text-xs text-muted-foreground text-center">
              Already have an account?{" "}
              <Link href="/login" className="underline hover:text-foreground">
                Sign in
              </Link>
            </p>
          </form>
        </CardBody>
      </Card>
    </main>
  );
}