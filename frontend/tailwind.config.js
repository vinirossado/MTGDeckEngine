/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./src/**/*.{html,ts}"],
  theme: {
    extend: {
      colors: {
        // Palette mirrors docs/explainer.html for visual consistency.
        bg:      "#07101f",
        panel:   "#0f1d33",
        panel2:  "#18253c",
        border2: "#2a3a55",
        accent:  "#6ee7ff",
        accent2: "#c4b5fd",
        ok:      "#86efac",
        warn:    "#fcd34d",
        muted:   "#93a3bd",
      },
    },
  },
  plugins: [],
};
