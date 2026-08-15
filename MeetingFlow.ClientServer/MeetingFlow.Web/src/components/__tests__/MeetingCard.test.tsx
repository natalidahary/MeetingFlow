import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import MeetingCard from "../MeetingCard";
import type { Meeting } from "../../types/models";

function buildMeeting(overrides: Partial<Meeting> = {}): Meeting {
  return {
    id: "meeting-1",
    title: "Test Meeting",
    description: "A meeting used for testing.",
    status: "Published",
    startsAt: "2026-01-01T10:00:00Z",
    endsAt: "2026-01-01T12:00:00Z",
    createdAt: "2025-12-01T00:00:00Z",
    venueId: "venue-1",
    venue: { id: "venue-1", name: "Main Hall", address: "1 Main St", city: "Metropolis", capacity: 100, meetings: [] },
    sessions: [],
    registrations: [],
    feedback: [],
    ...overrides,
  };
}

function renderMeetingCard(meeting: Meeting) {
  render(
    <MemoryRouter>
      <MeetingCard meeting={meeting} />
    </MemoryRouter>
  );
}

describe("MeetingCard badge", () => {
  it("renders a Published badge for a Published meeting", () => {
    renderMeetingCard(buildMeeting({ status: "Published" }));
    expect(screen.getByText("Published")).toBeInTheDocument();
  });

  it("renders a Draft badge for a Draft meeting", () => {
    renderMeetingCard(buildMeeting({ status: "Draft" }));
    expect(screen.getByText("Draft")).toBeInTheDocument();
  });

  it("renders a Cancelled badge for a Cancelled meeting", () => {
    renderMeetingCard(buildMeeting({ status: "Cancelled" }));
    expect(screen.getByText("Cancelled")).toBeInTheDocument();
  });
});
