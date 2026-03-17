async function addPolicy() {
  const data = {
    premium: Math.floor(Math.random() * 1_000_000),
    policyHolderId: Math.floor(Math.random() * 7_417),
  };

  const res = await fetch(
    "https://policymanager-api-fdhafuhzfagyajby.centralus-01.azurewebsites.net/api/policies",
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify(data),
    },
  );

  console.log({ status: res.status, text: await res.text() });
}

const policyRequests = [] as Promise<void>[];
for (let i = 0; i < 10_000; i++) {
  policyRequests.push(addPolicy());
}

await Promise.all(policyRequests);
