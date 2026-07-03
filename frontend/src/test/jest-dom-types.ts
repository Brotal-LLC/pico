// Vitest 4.x + jest-dom 6.x type augmentation
// The /vitest entry point types declare the module augmentation but
// aren't auto-discovered by tsc. This side-effect import pulls in the
// type declaration that augments vitest's Assertion interface.
import "@testing-library/jest-dom/vitest";