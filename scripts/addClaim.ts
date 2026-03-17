async function addClaim() {
  const data = {
    amount: Math.floor(Math.random() * 1_000_000),
    policyId: Math.floor(Math.random() * 5_000),
  };

  const res = await fetch(
    "https://policymanager-api-fdhafuhzfagyajby.centralus-01.azurewebsites.net/api/claims",
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

const claimRequests = [] as Promise<void>[];
for (let i = 0; i < 10_000; i++) {
  claimRequests.push(addClaim());
}

await Promise.all(claimRequests);
