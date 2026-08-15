import { describe, it, expect } from "vitest";
import { computeAverageRating } from "../rating";

describe("computeAverageRating", () => {
  it("averages multiple ratings and rounds to one decimal", () => {
    expect(computeAverageRating([{ rating: 5 }, { rating: 4 }, { rating: 3 }])).toBe("4.0");
  });

  it("returns N/A for an empty list", () => {
    expect(computeAverageRating([])).toBe("N/A");
  });

  it("returns the single rating for a list of one", () => {
    expect(computeAverageRating([{ rating: 1 }])).toBe("1.0");
  });
});
