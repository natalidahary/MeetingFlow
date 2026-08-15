import type { Feedback } from "../types/models";

export function computeAverageRating(feedback: Pick<Feedback, "rating">[]): string {
  if (!feedback.length) return "N/A";
  return (feedback.reduce((sum, f) => sum + f.rating, 0) / feedback.length).toFixed(1);
}
