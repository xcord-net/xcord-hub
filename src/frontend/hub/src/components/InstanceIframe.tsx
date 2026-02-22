import { onMount, onCleanup } from 'solid-js';

interface InstanceIframeProps {
  url: string;
  visible: boolean;
}

export default function InstanceIframe(props: InstanceIframeProps) {
  let iframeRef: HTMLIFrameElement | undefined;

  onMount(() => {
    // Send LeaveConversation message when iframe becomes hidden
    const handleVisibilityChange = () => {
      if (!props.visible && iframeRef?.contentWindow) {
        iframeRef.contentWindow.postMessage(
          { type: 'xcord_leave_conversation' },
          props.url
        );
      }
    };

    // Watch for visibility changes
    const unwatch = () => {};
    onCleanup(unwatch);
  });

  // Send LeaveConversation when iframe is hidden
  const handleLeaveOnHide = () => {
    if (!props.visible && iframeRef?.contentWindow) {
      iframeRef.contentWindow.postMessage(
        { type: 'xcord_leave_conversation' },
        props.url
      );
    }
  };

  return (
    <iframe
      ref={iframeRef}
      src={props.url}
      class="w-full h-full border-0"
      style={{
        display: props.visible ? 'block' : 'none',
      }}
      onLoad={handleLeaveOnHide}
      title="Instance"
    />
  );
}
