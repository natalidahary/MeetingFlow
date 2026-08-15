# Pre-Lecture Homework: Unit Testing in Practice

> **Goal:** Before the lecture, explore the MeetingFlow codebase and try to write
> tests for existing components. You will likely run into difficulties — that is
> the point. Bring your observations, frustrations, and questions to the lecture.

---

## Part 0 — Setup (~10 minutes)

Clone the repository and make sure the application runs.

### Frontend

```bash
cd MeetingFlow.ClientServer/MeetingFlow.Web
npm install
npm run dev
```

Open http://localhost:5173 and verify the app loads.

### Backend

```bash
cd MeetingFlow.ClientServer/MeetingFlow.Api
dotnet run
```

Open http://localhost:5062/api/meetings and verify JSON is returned.

---

## Part 1 — Read the code (~15 minutes)

Open the following files and read them carefully. No changes needed yet.

### Frontend

| File                                               | What to look at                                                                           |
| -------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `MeetingFlow.Web/src/components/MeetingCard.tsx`   | How does it decide which badge CSS class to use?                                          |
| `MeetingFlow.Web/src/components/MeetingTable.tsx`  | Same badge logic appears here — is it duplicated?                                         |
| `MeetingFlow.Web/src/pages/MeetingDetailsPage.tsx` | How does it compute the average rating? Where does it get its data?                       |
| `MeetingFlow.Web/src/pages/MeetingsPage.tsx`       | How does this page get its data? Could you render `MeetingCard` without a running server? |

**Answers:**

- `MeetingCard.tsx`: a ternary chain on `meeting.status` — `"Published"` → `badge-published`, `"Draft"` → `badge-draft`, anything else → `badge-cancelled`.
- `MeetingTable.tsx`: yes, duplicated — identical ternary, just on `ev.status` instead of `meeting.status`.
- `MeetingDetailsPage.tsx`: fetches the full `Meeting` (including nested `feedback[]`) once via `fetchMeeting(id)`; average rating is computed client-side as `sum(ratings) / count`, `.toFixed(1)`, or `"N/A"` if no feedback.
- `MeetingsPage.tsx`: calls `fetchMeetings()` → real HTTP GET `/meetings` on mount, no mock fallback. `MeetingCard` itself has no network dependency — it just takes a `meeting` prop, so it can be rendered with a hand-built fake object with no server running.

### Backend

| File                                                  | What to look at                                                                        |
| ----------------------------------------------------- | -------------------------------------------------------------------------------------- |
| `MeetingFlow.Api/Endpoints/RegistrationsEndpoints.cs` | What happens when a registration is created? Where does the current time come from?    |
| `MeetingFlow.Api/Endpoints/DashboardEndpoints.cs`     | How does it filter "upcoming" meetings? Could you test that filter without a database? |
| `MeetingFlow.Api/Models/Meeting.cs`                   | What is `Status`? What values can it have? (Hint: look at `SeedData.cs`)               |

**Answers:**

- `RegistrationsEndpoints.cs`: finds or creates an `Attendee` by email, creates a `Registration` with `PaymentStatus = "Pending"`, saves both in one `SaveChangesAsync()`, returns `201 Created`. Timestamp comes from `DateTimeOffset.UtcNow` — the server's real system clock, called directly (not injected/mockable).
- `DashboardEndpoints.cs`: pulls *all* meetings into memory, then filters in-memory for `StartsAt > DateTimeOffset.UtcNow && Status == "Published"`, sorts by `StartsAt`, takes 5. Not testable as-is without a DB since the filter is inline with the EF query; would need extracting into a pure function taking `(meetings, now)` to unit test without a database.
- `Meeting.cs`: `Status` is a plain `string`, no enum. Seed data shows three values in practice: `"Published"`, `"Draft"`, `"Cancelled"` — a convention, not enforced by the type.

### Reflection questions (write down your answers)

1. If you wanted to verify that a `Published` meeting gets `badge-published` and
   a `Draft` meeting gets `badge-draft` — what would you need to set up?

   **Answer:** A component test setup (e.g. React Testing Library) that renders `<MeetingCard meeting={...} />` directly with hand-built fake `Meeting` objects (`status: "Published"` and `status: "Draft"`), then asserts the badge `<span>`'s class. No server or database needed — just a router wrapper (`MemoryRouter`) since the component uses `<Link>`.

2. In `RegistrationsEndpoints.cs`, the registration timestamp is
   `DateTimeOffset.UtcNow`. If you wrote a test that checks the timestamp,
   would it be deterministic? Why or why not?

   **Answer:** No — `UtcNow` returns the real current time, different on every run, so an exact-equality assertion can never reliably pass. You could only assert "close to now" within a tolerance, or better, inject a clock abstraction (e.g. `TimeProvider`) so tests can supply a fixed, known time.

3. `MeetingDetailsPage.tsx` fetches data with `fetchMeeting(id)`, computes the
   average rating, and renders everything. If you only want to test the average
   rating calculation, what is the minimum setup you would need?

   **Answer:** Best option: extract the calculation into a standalone pure function (e.g. `computeAverageRating(feedback)`) and unit test it directly with plain arrays of `{ rating }` objects — no React, no fetch, no DOM. Without extracting it, you'd have to mock `fetchMeeting`, wrap the page in a router context, wait for loading to resolve, and assert on rendered text — much more setup just to test one line of math.

---

## Part 2 — Set up a test runner (~10 minutes)

### Frontend: Add Vitest

Add testing dependencies to `MeetingFlow.Web/package.json`:

```bash
cd MeetingFlow.ClientServer/MeetingFlow.Web
npm install -D vitest @testing-library/react @testing-library/user-event @testing-library/jest-dom jsdom
```

Create `vitest.config.ts` in the `MeetingFlow.Web` folder:

```ts
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
  },
});
```

Create `vitest.setup.ts`:

```ts
import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(() => {
  cleanup();
});
```

Add a test script to `package.json`:

```json
"scripts": {
  "test": "vitest run",
  "test:watch": "vitest"
}
```

Verify it works: `npm test` (it should say "no test files found" — that is correct for now).

### Backend: Add a test project

```bash
cd MeetingFlow.ClientServer
dotnet new xunit -n MeetingFlow.Api.Tests
```

Verify it works: `cd MeetingFlow.Api.Tests && dotnet test` (the default template test should pass).

---

## Part 3 — Try to test the badge logic (~20 minutes)

The badge logic appears in `MeetingCard.tsx` (line 10):

```tsx
const badgeClass =
  meeting.status === "Published" ? "badge-published" : meeting.status === "Draft" ? "badge-draft" : "badge-cancelled";
```

### Your task

Write a test file `src/components/__tests__/MeetingCard.test.tsx` that verifies:

1. A meeting with `status: "Published"` renders a badge with the text "Published"
2. A meeting with `status: "Draft"` renders a badge with the text "Draft"
3. A meeting with `status: "Cancelled"` renders a badge with the text "Cancelled"

**Hints:**

- `MeetingCard` expects a `meeting` prop of type `Meeting` — you will need to
  construct a full `Meeting` object even though the component only uses a few fields.
- The component uses `<Link>` from `react-router-dom`, so you need to wrap it
  in a `<MemoryRouter>` for the test. Example:

```tsx
import { MemoryRouter } from 'react-router-dom';
import { render, screen } from '@testing-library/react';

render(
  <MemoryRouter>
    <MeetingCard meeting={...} />
  </MemoryRouter>
);
```

### Questions to think about while working

- How many fields did you have to fake just to test the badge?
- Could you test the badge logic without rendering React at all?
- If someone changes the badge logic, would your test catch it?
  What if they change the CSS class but not the text?

**Answers:**

- **How many fields did you have to fake just to test the badge?** All 11 required fields on `Meeting` (`id`, `title`, `description`, `status`, `startsAt`, `endsAt`, `createdAt`, `venueId`, `sessions`, `registrations`, `feedback`), even though the badge only depends on `status`. That's the cost of the type mirroring the full DB entity instead of a component-specific shape.
- **Could you test the badge logic without rendering React at all?** Yes, in principle — the ternary is pure string logic with no React dependency. If it were extracted into a function like `getBadgeClass(status)`, it could be unit tested directly with no `render`/`MemoryRouter`/DOM at all. As written, it's inlined in the component, so a full render is the only way to exercise it today.
- **If someone changes the badge logic, would your test catch it? What about CSS class vs. text?** My test only asserts on the visible *text* (`"Published"`, `"Draft"`, `"Cancelled"`), not the CSS class. So if someone changed `badge-published` to a different/wrong class name while leaving the text alone, the test would still pass — it would not catch that regression. Catching it requires an explicit assertion like `expect(screen.getByText("Published")).toHaveClass("badge-published")`.

---

## Part 4 — Try to test registration validation (~20 minutes)

Look at `RegistrationsEndpoints.cs`. The endpoint creates a registration, but
it doesn't validate much. Imagine we add these rules:

> - A registration should only be accepted if the meeting status is `"Published"`.
> - A registration should be rejected if the meeting's venue is at full capacity.

### Your task

In your `MeetingFlow.Api.Tests` project, write a test class that verifies:

1. Registering for a `Published` meeting succeeds.
2. Registering for a `Draft` meeting is rejected.
3. Registering for a full meeting (registration count = venue capacity) is rejected.

**You will run into problems. That is expected.** Write down what blocks you:

- Can you call the endpoint logic without starting the web server?
- Can you test the validation without hitting the real database?
- Can you control what `DateTimeOffset.UtcNow` returns?
- If you could extract the validation into a separate method/class,
  what would its signature look like?

**Sketch it out** — even pseudocode or comments are valuable. Example:

```csharp
// What I wish I could write:
//
// var meeting = new { Status = "Draft", RegistrationCount = 0, Capacity = 100 };
// var result = SomeValidator.Validate(meeting);
// Assert.Equal("Rejected", result);
//
// But I can't because... [write why]
```

**What I found:** created `MeetingFlow.Api.Tests/RegistrationValidationTests.cs` with a project reference to `MeetingFlow.Api` and tried to write the three tests for real.

- **Can you call the endpoint logic without starting the web server?** No. The whole create-registration handler is one anonymous lambda passed straight to `app.MapPost(...)` inside `MapRegistrationsEndpoints` — it's never assigned to a variable or a named method, so there's no symbol a test can import and call directly. The only way to exercise it today is over real HTTP (`TestServer`/`WebApplicationFactory`), which drags in routing, model binding, and EF Core just to check an if/else.
- **Checked one assumption before going further:** many tutorials warn that `WebApplicationFactory<Program>` won't compile because top-level-statement `Program` is `internal`. I tested this directly (`typeof(Program).IsPublic`) — it's actually `true` here, and `WebApplicationFactory<Program>` compiles fine. So *that* specific blocker doesn't apply in this project; the real blockers are elsewhere (below).
- **Can you test the validation without hitting the real database?** The validation doesn't exist yet — `RegistrationsEndpoints.cs` accepts every request unconditionally, so there's nothing in production code that would ever return "Rejected." Even if it did exist, going through `WebApplicationFactory` as-is would hit the real dev file `meetingflow_api.db` (hardcoded in `Program.cs`, not swappable via config) and reseed it — you'd need `WithWebHostBuilder(b => b.ConfigureServices(...))` to swap in an isolated DbContext, which is extra plumbing this exercise doesn't hand you for free.
- **Can you control what `DateTimeOffset.UtcNow` returns?** No — same issue as Part 1's reflection question. `RegisteredAt = DateTimeOffset.UtcNow` is called directly in the handler, no clock is injected.
- **If you could extract the validation, what would its signature look like?**
  ```csharp
  public enum ValidationResult { Accepted, Rejected }
  public static class RegistrationValidator
  {
      public static ValidationResult Validate(string meetingStatus, int registrationCount, int venueCapacity)
          => meetingStatus != "Published"       ? ValidationResult.Rejected
           : registrationCount >= venueCapacity ? ValidationResult.Rejected
           : ValidationResult.Accepted;
  }
  ```
  A pure function over three primitives, no `DbContext`, no HTTP, no clock — that's what would make the three required scenarios trivial to test in isolation.

The core blocker isn't test infrastructure — it's that the feature described in the task (status + capacity validation) doesn't exist in the code at all yet, so no test setup can make "Registering for a Draft meeting is rejected" pass against real code today.

---

## Part 5 — Bonus: Average rating calculation (~10 minutes, optional)

`MeetingDetailsPage.tsx` computes the average feedback rating (lines 34–36):

```tsx
const avgRating = meeting.feedback?.length
  ? (meeting.feedback.reduce((sum, f) => sum + f.rating, 0) / meeting.feedback.length).toFixed(1)
  : "N/A";
```

### Your task

Try to test this calculation without rendering the page.

- Can you extract it into a standalone function?
- If yes, write a test for it:
  - `[5, 4, 3]` → `"4.0"`
  - `[]` → `"N/A"`
  - `[1]` → `"1.0"`
- If not, write down what stops you.

**What I found:** unlike Part 4's backend validation, this one *could* be extracted cleanly — it's pure math with zero dependency on React, fetch, or the DOM.

- Created `src/utils/rating.ts` with a standalone `computeAverageRating(feedback: { rating: number }[]): string`.
- Updated `MeetingDetailsPage.tsx` to call it instead of doing the calculation inline (`const avgRating = computeAverageRating(meeting.feedback ?? [])`).
- Wrote `src/utils/__tests__/rating.test.ts` with exactly the three required cases — all pass, no `render`, no `MemoryRouter`, no mocked fetch:
  - `[{rating:5},{rating:4},{rating:3}]` → `"4.0"`
  - `[]` → `"N/A"`
  - `[{rating:1}]` → `"1.0"`

The difference from Part 4 is the point of this exercise: the average-rating logic was always pure and self-contained, it just happened to be written *inline inside a component*. Pulling it out was a pure refactor — no new behavior needed. The registration validation in Part 4 couldn't be tested no matter how it was extracted, because the behavior itself doesn't exist in the code yet. Same lesson ("pull logic out of the component/handler into a plain function"), but one had something to extract and the other didn't.

---

## What to bring to the lecture

1. **Your test files** (even if they don't work or are incomplete)
2. **Your answers** to the reflection questions in Parts 1, 3, and 4
3. **A list of things that were hard** — these are exactly what the lecture covers

We will review common patterns for making code testable, and you will see how
the same components can be restructured so the logic becomes trivial to test
without heavy mocking or infrastructure setup.

---

## Summary of deliverables

| #   | Task                                       | Time   | Required? |
| --- | ------------------------------------------ | ------ | --------- |
| 0   | Setup — run the app                        | 10 min | Yes       |
| 1   | Read the code, answer reflection questions | 15 min | Yes       |
| 2   | Set up Vitest + xUnit test project         | 10 min | Yes       |
| 3   | Test the badge logic in MeetingCard        | 20 min | Yes       |
| 4   | Test registration validation (sketch)      | 20 min | Yes       |
| 5   | Extract and test average rating            | 10 min | Bonus     |

**Total: ~75 minutes** (65 without the bonus)
