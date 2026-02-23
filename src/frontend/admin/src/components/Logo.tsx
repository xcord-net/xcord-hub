export default function Logo(props: { class?: string }) {
  return (
    <span class={`inline-flex items-center gap-0.5 ${props.class ?? ''}`}>
      <svg
        viewBox="41 45 430 422"
        fill="none"
        stroke="currentColor"
        stroke-width="29"
        stroke-linecap="round"
        stroke-linejoin="round"
        class="h-[1.1em] w-auto mr-[-5px]"
        aria-hidden="true"
      >
        <path d="M58,141 C146,209 146,303 58,371" />
        <path d="M116,102 C205,180 205,332 116,410" />
        <path d="M175,62 C263,160 263,352 175,450" />
        <path d="M337,62 C249,160 249,352 337,450" />
        <path d="M396,102 C307,180 307,332 396,410" />
        <path d="M454,141 C366,209 366,303 454,371" />
      </svg>
      ord
    </span>
  );
}
