import { createEffect } from 'solid-js';

interface InstanceIframeProps {
  url: string;
  visible: boolean;
}

export default function InstanceIframe(props: InstanceIframeProps) {
  let iframeRef: HTMLIFrameElement | undefined;

  // When the iframe transitions from visible to hidden, notify the instance
  // so it can leave the active conversation / voice channel cleanly.
  createEffect((prevVisible: boolean) => {
    const visible = props.visible;
    if (prevVisible && !visible && iframeRef?.contentWindow) {
      iframeRef.contentWindow.postMessage(
        { type: 'xcord_leave_conversation' },
        props.url
      );
    }
    return visible;
  }, true);

  return (
    <iframe
      ref={iframeRef}
      src={props.url}
      class="w-full h-full border-0"
      style={{
        display: props.visible ? 'block' : 'none',
      }}
      title="Instance"
    />
  );
}
